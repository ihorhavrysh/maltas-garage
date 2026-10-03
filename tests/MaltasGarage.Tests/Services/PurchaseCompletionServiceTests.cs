using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Services;
using MaltasGarage.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaltasGarage.Tests.Services;

/// <summary>
/// The buyer's return from Stripe and the webhook both finish a purchase. Whichever comes
/// second must find it done, and money is refunded only when the purchase really failed.
/// </summary>
public class PurchaseCompletionServiceTests
{
    private static PurchaseCompletionService CreateService(ApplicationDbContext ctx, RecordingPaymentService payment)
    {
        var messaging = new NoOpMessagingService();
        var email = new NoOpEmailNotificationService();
        return new PurchaseCompletionService(
            ctx,
            new OrderService(ctx, messaging, email, payment, TimeProvider.System),
            new BiddingService(ctx, messaging, email, TimeProvider.System),
            new PriceOfferService(ctx, messaging, email, TimeProvider.System),
            payment,
            messaging,
            NullLogger<PurchaseCompletionService>.Instance);
    }

    private static async Task<(Listing Listing, UserProfile Buyer)> SeedListingAsync(
        ApplicationDbContext ctx, ListingStatus status = ListingStatus.Active, Guid? categoryId = null)
    {
        var seller = new UserProfile { Id = Guid.NewGuid(), UserId = $"seller-{Guid.NewGuid():N}" };
        var buyer = new UserProfile { Id = Guid.NewGuid(), UserId = $"buyer-{Guid.NewGuid():N}" };
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = seller.Id,
            CategoryId = categoryId ?? Guid.NewGuid(),
            Title = "Purchase listing",
            MinPrice = 50m,
            DesiredPrice = 100m,
            CurrentPrice = 50m,
            Status = status,
            SellByDate = DateTime.UtcNow.AddDays(3),
            AuctionStartDate = DateTime.UtcNow.AddDays(-1)
        };
        ctx.UserProfiles.AddRange(seller, buyer);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();
        return (listing, buyer);
    }

    private static ConfirmedPayment Paid(string purpose, Guid subjectId, Guid payerId, decimal amount, string intentId = "pi_test") =>
        new(intentId, amount, PaymentMetadata.For(purpose, subjectId, payerId));

    [Fact]
    public async Task BuyNow_CreatesPaidOrderWithItsPayment()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var (listing, buyer) = await SeedListingAsync(ctx);

        var result = await CreateService(ctx, payment)
            .CompleteAsync(Paid(PaymentMetadata.BuyNow, listing.Id, buyer.Id, 100m), DeliveryMethod.MaltaPost);

        Assert.True(result.Succeeded);
        var order = await ctx.Orders.Include(o => o.Payment).SingleAsync();
        Assert.Equal(result.OrderId, order.Id);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(DeliveryMethod.MaltaPost, order.DeliveryMethod);
        Assert.Equal(PaymentStatus.Captured, order.Payment!.Status);
        Assert.Equal("pi_test", order.Payment.StripePaymentIntentId);
        Assert.Equal(ListingStatus.Sold, (await ctx.Listings.FindAsync(listing.Id))!.Status);
        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public async Task BuyNow_CompletedTwice_KeepsOneOrderAndRefundsNothing()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var (listing, buyer) = await SeedListingAsync(ctx);
        var service = CreateService(ctx, payment);
        var paid = Paid(PaymentMetadata.BuyNow, listing.Id, buyer.Id, 100m);

        var first = await service.CompleteAsync(paid, DeliveryMethod.HandToHand);
        var second = await service.CompleteAsync(paid, DeliveryMethod.HandToHand);

        Assert.True(second.Succeeded);
        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Single(await ctx.Orders.ToListAsync());
        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public async Task Webhook_ThenBuyerReturns_AppliesTheBuyersDeliveryChoice()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var (listing, buyer) = await SeedListingAsync(ctx);
        var service = CreateService(ctx, payment);
        var paid = Paid(PaymentMetadata.BuyNow, listing.Id, buyer.Id, 100m);

        await service.CompleteAsync(paid);   // the webhook knows no delivery method
        Assert.True((await ctx.Orders.SingleAsync()).DeliveryMethodPending);

        await service.CompleteAsync(paid, DeliveryMethod.MaltaPost);

        var order = await ctx.Orders.SingleAsync();
        Assert.False(order.DeliveryMethodPending);
        Assert.Equal(DeliveryMethod.MaltaPost, order.DeliveryMethod);
    }

    [Fact]
    public async Task BuyNow_OnSoldListing_RefundsThePayment()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var (listing, buyer) = await SeedListingAsync(ctx, ListingStatus.Sold);

        var result = await CreateService(ctx, payment)
            .CompleteAsync(Paid(PaymentMetadata.BuyNow, listing.Id, buyer.Id, 100m), DeliveryMethod.HandToHand);

        Assert.False(result.Succeeded);
        Assert.Equal(("pi_test", (decimal?)null), Assert.Single(payment.Refunds));
        Assert.Empty(await ctx.Orders.ToListAsync());
    }

    [Fact]
    public async Task BuyNow_DuringAuction_RefundsTheTopBid()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var (listing, buyer) = await SeedListingAsync(ctx, ListingStatus.AuctionPhase);
        ctx.Bids.Add(new Bid { Id = Guid.NewGuid(), ListingId = listing.Id, BidderId = Guid.NewGuid(), Amount = 60m, StripePaymentIntentId = "pi_bid" });
        await ctx.SaveChangesAsync();

        var result = await CreateService(ctx, payment)
            .CompleteAsync(Paid(PaymentMetadata.BuyNow, listing.Id, buyer.Id, 100m), DeliveryMethod.HandToHand);

        Assert.True(result.Succeeded);
        Assert.Equal(("pi_bid", (decimal?)null), Assert.Single(payment.Refunds));
    }

    [Fact]
    public async Task BuyNow_CompletesOwnOfferAndCancelsOtherBuyersOffers()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var (listing, buyer) = await SeedListingAsync(ctx);
        var own = new PriceOffer { Id = Guid.NewGuid(), ListingId = listing.Id, BuyerId = buyer.Id, SellerId = listing.SellerId, Amount = 80m, Status = PriceOfferStatus.Accepted, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        var other = new PriceOffer { Id = Guid.NewGuid(), ListingId = listing.Id, BuyerId = Guid.NewGuid(), SellerId = listing.SellerId, Amount = 70m, Status = PriceOfferStatus.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        ctx.PriceOffers.AddRange(own, other);
        await ctx.SaveChangesAsync();

        var paid = new ConfirmedPayment("pi_offer", 80m, PaymentMetadata.For(PaymentMetadata.BuyNow, listing.Id, buyer.Id, own.Id));
        await CreateService(ctx, payment).CompleteAsync(paid, DeliveryMethod.HandToHand);

        Assert.Equal(PriceOfferStatus.Completed, (await ctx.PriceOffers.FindAsync(own.Id))!.Status);
        Assert.Equal(PriceOfferStatus.Cancelled, (await ctx.PriceOffers.FindAsync(other.Id))!.Status);
    }

    [Fact]
    public async Task Bid_CompletedTwice_RecordsOneBidAndRefundsNothing()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var (listing, buyer) = await SeedListingAsync(ctx, ListingStatus.AuctionPhase);
        var service = CreateService(ctx, payment);
        var paid = Paid(PaymentMetadata.Bid, listing.Id, buyer.Id, 60m);

        Assert.True((await service.CompleteAsync(paid)).Succeeded);
        Assert.True((await service.CompleteAsync(paid)).Succeeded);

        Assert.Single(await ctx.Bids.ToListAsync());
        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public async Task Bid_ThatOutbidsAnother_RefundsThePreviousLeader()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var (listing, buyer) = await SeedListingAsync(ctx, ListingStatus.AuctionPhase);
        ctx.Bids.Add(new Bid { Id = Guid.NewGuid(), ListingId = listing.Id, BidderId = Guid.NewGuid(), Amount = 55m, StripePaymentIntentId = "pi_old" });
        await ctx.SaveChangesAsync();

        var result = await CreateService(ctx, payment).CompleteAsync(Paid(PaymentMetadata.Bid, listing.Id, buyer.Id, 70m));

        Assert.True(result.Succeeded);
        Assert.Equal(("pi_old", (decimal?)null), Assert.Single(payment.Refunds));
    }

    [Fact]
    public async Task RefundedListing_CanBeBoughtAgain()
    {
        // Real database: a refund puts the item back on sale, and the second order for the same
        // listing must not hit a unique index on Orders.ListingId
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var ctx = TestDbContextFactory.CreateRelational(connection);
        var category = new Category { Id = Guid.NewGuid(), Name = "Sports", Slug = "sports" };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var payment = new RecordingPaymentService();
        var (listing, firstBuyer) = await SeedListingAsync(ctx, categoryId: category.Id);
        var secondBuyer = new UserProfile { Id = Guid.NewGuid(), UserId = "second-buyer" };
        ctx.UserProfiles.Add(secondBuyer);
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx, payment);

        var first = await service.CompleteAsync(Paid(PaymentMetadata.BuyNow, listing.Id, firstBuyer.Id, 100m, "pi_first"), DeliveryMethod.HandToHand);
        await new OrderService(ctx, new NoOpMessagingService(), new NoOpEmailNotificationService(), payment, TimeProvider.System)
            .RefundBuyerAsync(first.OrderId!.Value);
        var second = await service.CompleteAsync(Paid(PaymentMetadata.BuyNow, listing.Id, secondBuyer.Id, 100m, "pi_second"), DeliveryMethod.HandToHand);

        Assert.True(second.Succeeded, second.Error);
        Assert.Equal(2, await ctx.Orders.CountAsync(o => o.ListingId == listing.Id));
    }

    [Fact]
    public async Task PaymentWithoutMarketplaceMetadata_IsIgnored()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();

        var result = await CreateService(ctx, payment)
            .CompleteAsync(new ConfirmedPayment("pi_other", 10m, new Dictionary<string, string>()));

        Assert.False(result.Succeeded);
        Assert.Empty(payment.Refunds);
    }
}
