using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Services;
using MaltasGarage.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaltasGarage.Tests.Services;

/// <summary>
/// The free App Service tier sleeps, so no timer is guaranteed to fire. These tests pin down
/// that transitions are applied on read, caught up by a sweep, and never applied twice.
/// </summary>
public class ListingLifecycleServiceTests
{
    private static readonly NoOpMessagingService _messaging = new();
    private static readonly NoOpEmailNotificationService _email = new();

    private readonly TestTimeProvider _clock = new();
    private readonly string _db = Guid.NewGuid().ToString();

    private ListingLifecycleService CreateService(ApplicationDbContext ctx, RecordingPaymentService? payments = null)
    {
        var paymentService = payments ?? new RecordingPaymentService();
        var orders = new OrderService(ctx, _messaging, _email, paymentService, _clock);
        return new ListingLifecycleService(ctx,
            new PriceOfferService(ctx, _messaging, _email, _clock),
            new BundleOfferService(ctx, _messaging, _email),
            orders, _messaging, _email, _clock, NullLogger<ListingLifecycleService>.Instance);
    }

    private async Task<(UserProfile Seller, UserProfile Buyer)> SeedUsersAsync(ApplicationDbContext ctx)
    {
        var seller = new UserProfile { Id = Guid.NewGuid(), UserId = "seller", StripeAccountId = "acct_test" };
        var buyer = new UserProfile { Id = Guid.NewGuid(), UserId = "buyer" };
        ctx.UserProfiles.AddRange(seller, buyer);
        await ctx.SaveChangesAsync();
        return (seller, buyer);
    }

    private Listing NewListing(UserProfile seller, ListingStatus status, TimeSpan endsIn, bool auctionStarted = true) => new()
    {
        Id = Guid.NewGuid(),
        SellerId = seller.Id,
        CategoryId = Guid.NewGuid(),
        Title = "Road bike",
        DesiredPrice = 400m,
        MinPrice = 200m,
        CurrentPrice = 400m,
        AuctionEnabled = true,
        Status = status,
        SellByDate = _clock.Now.UtcDateTime + endsIn,
        AuctionStartDate = _clock.Now.UtcDateTime + (auctionStarted ? TimeSpan.FromDays(-1) : TimeSpan.FromDays(1))
    };

    private static Bid NewBid(Listing listing, UserProfile bidder, decimal amount, string? paymentIntent = "pi_test") => new()
    {
        Id = Guid.NewGuid(), ListingId = listing.Id, BidderId = bidder.Id, Amount = amount, StripePaymentIntentId = paymentIntent
    };

    [Fact]
    public async Task EnsureCurrent_ClosesAnEndedAuction_AndCreatesThePaidOrderForTheWinner()
    {
        var ctx = TestDbContextFactory.Create(_db);
        var (seller, buyer) = await SeedUsersAsync(ctx);
        var listing = NewListing(seller, ListingStatus.AuctionPhase, TimeSpan.FromMinutes(-1));
        ctx.Listings.Add(listing);
        ctx.Bids.AddRange(NewBid(listing, buyer, 250m), NewBid(listing, buyer, 210m));
        await ctx.SaveChangesAsync();

        await CreateService(ctx).EnsureCurrentAsync(listing.Id);

        var reloaded = await ctx.Listings.Include(l => l.Bids).SingleAsync();
        Assert.Equal(ListingStatus.Sold, reloaded.Status);
        Assert.Equal(250m, reloaded.CurrentPrice);
        Assert.True(reloaded.Bids.Single(b => b.Amount == 250m).IsWinningBid);

        var order = await ctx.Orders.Include(o => o.Payment).SingleAsync();
        Assert.Equal(buyer.Id, order.BuyerId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(25m, order.PlatformFee);
        Assert.Equal(PaymentStatus.Captured, order.Payment!.Status);
    }

    [Fact]
    public async Task EnsureCurrent_BeforeTheEnd_ChangesNothing()
    {
        var ctx = TestDbContextFactory.Create(_db);
        var (seller, buyer) = await SeedUsersAsync(ctx);
        var listing = NewListing(seller, ListingStatus.AuctionPhase, TimeSpan.FromHours(3));
        ctx.Listings.Add(listing);
        ctx.Bids.Add(NewBid(listing, buyer, 250m));
        await ctx.SaveChangesAsync();

        await CreateService(ctx).EnsureCurrentAsync(listing.Id);

        Assert.Equal(ListingStatus.AuctionPhase, (await ctx.Listings.SingleAsync()).Status);
        Assert.Empty(ctx.Orders);
    }

    [Fact]
    public async Task Sweep_CatchesUpEveryDueTransition_AfterTheAppWasAsleep()
    {
        var ctx = TestDbContextFactory.Create(_db);
        var (seller, buyer) = await SeedUsersAsync(ctx);
        var toOpen = NewListing(seller, ListingStatus.Active, TimeSpan.FromDays(3));
        var toExpire = NewListing(seller, ListingStatus.AuctionPhase, TimeSpan.FromHours(-2));
        var toClose = NewListing(seller, ListingStatus.AuctionPhase, TimeSpan.FromHours(-5));
        var notYet = NewListing(seller, ListingStatus.Active, TimeSpan.FromDays(10), auctionStarted: false);
        var showcase = NewListing(seller, ListingStatus.AuctionPhase, TimeSpan.FromHours(-1));
        showcase.IsShowcase = true;
        ctx.Listings.AddRange(toOpen, toExpire, toClose, notYet, showcase);
        ctx.Bids.Add(NewBid(toClose, buyer, 300m));
        ctx.PriceOffers.Add(new PriceOffer
        {
            Id = Guid.NewGuid(), ListingId = notYet.Id, BuyerId = buyer.Id, SellerId = seller.Id,
            Amount = 50m, Status = PriceOfferStatus.Pending, ExpiresAt = _clock.Now.UtcDateTime.AddHours(-1)
        });
        await ctx.SaveChangesAsync();

        var result = await CreateService(ctx).SweepAsync();

        Assert.Equal(new LifecycleSweepResultShape(1, 1, 1, 1), Shape(result));
        var statuses = await ctx.Listings.ToDictionaryAsync(l => l.Id, l => l.Status);
        Assert.Equal(ListingStatus.AuctionPhase, statuses[toOpen.Id]);
        Assert.Equal(ListingStatus.Expired, statuses[toExpire.Id]);
        Assert.Equal(ListingStatus.Sold, statuses[toClose.Id]);
        Assert.Equal(ListingStatus.Active, statuses[notYet.Id]);
        Assert.Equal(ListingStatus.AuctionPhase, statuses[showcase.Id]);
        Assert.Equal(PriceOfferStatus.Expired, (await ctx.PriceOffers.SingleAsync()).Status);
    }

    [Fact]
    public async Task Sweep_RunTwice_AppliesEachTransitionOnce()
    {
        var ctx = TestDbContextFactory.Create(_db);
        var (seller, buyer) = await SeedUsersAsync(ctx);
        var listing = NewListing(seller, ListingStatus.AuctionPhase, TimeSpan.FromMinutes(-10));
        ctx.Listings.Add(listing);
        ctx.Bids.Add(NewBid(listing, buyer, 300m));
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx);

        var first = await service.SweepAsync();
        var second = await service.SweepAsync();
        await service.EnsureCurrentAsync(listing.Id);

        Assert.Equal(1, first.AuctionsClosed);
        Assert.Equal(0, second.Total);
        Assert.Single(ctx.Orders);
    }

    [Fact]
    public async Task SweepAndPageRequestRacing_CreateExactlyOneOrder()
    {
        // A relational database, because the guarantee relies on the order insert and the
        // listing update committing or rolling back together. Two contexts stand in for the
        // background sweep and a page request that both loaded the ended auction.
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var setup = TestDbContextFactory.CreateRelational(connection);
        var category = new Category { Id = Guid.NewGuid(), Name = "Sports", Slug = "sports" };
        setup.Categories.Add(category);
        var (seller, buyer) = await SeedUsersAsync(setup);
        var listing = NewListing(seller, ListingStatus.AuctionPhase, TimeSpan.FromMinutes(-1));
        listing.CategoryId = category.Id;
        setup.Listings.Add(listing);
        setup.Bids.Add(NewBid(listing, buyer, 300m));
        await setup.SaveChangesAsync();

        var sweepCtx = TestDbContextFactory.CreateRelational(connection);
        var requestCtx = TestDbContextFactory.CreateRelational(connection);
        var requestService = CreateService(requestCtx);
        var staleCopy = await requestCtx.Listings.Include(l => l.Bids).SingleAsync(l => l.Id == listing.Id);

        await CreateService(sweepCtx).SweepAsync();

        // The request still holds its stale copy and now tries to close the same auction
        staleCopy.Status = ListingStatus.Sold;
        requestCtx.Orders.Add(new Order { Id = Guid.NewGuid(), ListingId = listing.Id, BuyerId = buyer.Id, SellerId = seller.Id });
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => requestCtx.SaveChangesAsync());

        // And through the service the late worker simply finds nothing left to do
        requestCtx.ChangeTracker.Clear();
        await requestService.EnsureCurrentAsync(listing.Id);

        var verify = TestDbContextFactory.CreateRelational(connection);
        Assert.Single(verify.Orders);
        Assert.Equal(ListingStatus.Sold, (await verify.Listings.SingleAsync()).Status);
    }

    [Fact]
    public async Task EscrowAutoRelease_IsIdempotent_AndAlwaysUsesTheOrderAsTransferKey()
    {
        var ctx = TestDbContextFactory.Create(_db);
        var (seller, buyer) = await SeedUsersAsync(ctx);
        var listing = NewListing(seller, ListingStatus.Sold, TimeSpan.FromDays(-10));
        var order = new Order
        {
            Id = Guid.NewGuid(), ListingId = listing.Id, BuyerId = buyer.Id, SellerId = seller.Id,
            FinalPrice = 100m, PlatformFee = 10m, SellerPayout = 90m,
            Status = OrderStatus.Shipped, DeliveryMethod = DeliveryMethod.MaltaPost
        };
        ctx.Listings.Add(listing);
        ctx.Orders.Add(order);
        ctx.Payments.Add(new Payment { Id = Guid.NewGuid(), OrderId = order.Id, StripePaymentIntentId = "pi_test", Amount = 100m, Status = PaymentStatus.Captured });
        ctx.Shipments.Add(new Shipment { Id = Guid.NewGuid(), OrderId = order.Id, ShippedAt = _clock.Now.UtcDateTime.AddDays(-6) });
        await ctx.SaveChangesAsync();
        var payments = new RecordingPaymentService();
        var service = CreateService(ctx, payments);

        var first = await service.SweepAsync();
        var second = await service.SweepAsync();

        Assert.Equal(1, first.EscrowsReleased);
        Assert.Equal(0, second.EscrowsReleased);
        Assert.Equal(new[] { order.Id.ToString() }, payments.TransferGroups);
        Assert.Equal(OrderStatus.Completed, (await ctx.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task AcceptingAnOfferPastItsExpiry_FailsEvenIfNoSweepRan()
    {
        var ctx = TestDbContextFactory.Create(_db);
        var (seller, buyer) = await SeedUsersAsync(ctx);
        var listing = NewListing(seller, ListingStatus.Active, TimeSpan.FromDays(10), auctionStarted: false);
        var offer = new PriceOffer
        {
            Id = Guid.NewGuid(), ListingId = listing.Id, BuyerId = buyer.Id, SellerId = seller.Id,
            Amount = 50m, Status = PriceOfferStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };
        ctx.Listings.Add(listing);
        ctx.PriceOffers.Add(offer);
        await ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PriceOfferService(ctx, _messaging, _email, _clock).AcceptOfferAsync(offer.Id, seller.Id));

        Assert.Equal("This offer has expired.", ex.Message);
        Assert.Equal(PriceOfferStatus.Expired, (await ctx.PriceOffers.SingleAsync()).Status);
    }

    private record LifecycleSweepResultShape(int Opened, int Expired, int Closed, int OffersExpired);

    private static LifecycleSweepResultShape Shape(Application.Common.Interfaces.LifecycleSweepResult r) =>
        new(r.AuctionsOpened, r.ListingsExpired, r.AuctionsClosed, r.OffersExpired);
}
