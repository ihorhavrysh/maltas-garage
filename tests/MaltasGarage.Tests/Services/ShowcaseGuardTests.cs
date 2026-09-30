using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Domain.Exceptions;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Services;
using MaltasGarage.Tests.Stubs;

namespace MaltasGarage.Tests.Services;

/// <summary>
/// Showcase listings must stay intact no matter which service path a visitor reaches.
/// </summary>
public class ShowcaseGuardTests
{
    private static readonly NoOpMessagingService _messaging = new();
    private static readonly NoOpEmailNotificationService _emailNotifications = new();
    private static readonly NoOpPaymentService _payment = new();

    private static async Task<(ApplicationDbContext Ctx, Listing Listing, Guid BuyerId)> SeedShowcaseAsync(ListingStatus status)
    {
        var ctx = TestDbContextFactory.Create();
        var seller = new UserProfile { Id = Guid.NewGuid(), UserId = "seller", StripeAccountId = "acct_test" };
        var buyer = new UserProfile { Id = Guid.NewGuid(), UserId = "buyer" };
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = seller.Id,
            CategoryId = Guid.NewGuid(),
            Title = "Showcase item",
            MinPrice = 50m,
            DesiredPrice = 100m,
            CurrentPrice = 50m,
            Status = status,
            IsShowcase = true,
            SellByDate = DateTime.UtcNow.AddDays(3),
            AuctionStartDate = DateTime.UtcNow.AddDays(-1)
        };
        ctx.UserProfiles.AddRange(seller, buyer);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();
        return (ctx, listing, buyer.Id);
    }

    [Fact]
    public void EnsureNotShowcase_ThrowsOnlyForShowcaseListings()
    {
        Assert.Throws<ShowcaseListingException>(() => new Listing { IsShowcase = true }.EnsureNotShowcase());
        new Listing { IsShowcase = false }.EnsureNotShowcase();
    }

    [Fact]
    public async Task PlaceBid_OnShowcase_IsRejectedAndPriceUnchanged()
    {
        var (ctx, listing, buyerId) = await SeedShowcaseAsync(ListingStatus.AuctionPhase);
        var service = new BiddingService(ctx, _messaging, _emailNotifications);

        var result = await service.PlaceBidAsync(listing.Id, buyerId, 60m);

        Assert.False(result.Success);
        Assert.Equal(ShowcaseListingException.DefaultMessage, result.Error);
        Assert.Empty(ctx.Bids);
        Assert.Equal(50m, (await ctx.Listings.FindAsync(listing.Id))!.CurrentPrice);
    }

    [Fact]
    public async Task CreateOrder_OnShowcase_ThrowsAndListingStaysActive()
    {
        var (ctx, listing, buyerId) = await SeedShowcaseAsync(ListingStatus.Active);
        var service = new OrderService(ctx, _messaging, _emailNotifications, _payment);

        await Assert.ThrowsAsync<ShowcaseListingException>(
            () => service.CreateOrderAsync(listing.Id, buyerId, 100m, DeliveryMethod.MaltaPost));

        Assert.Empty(ctx.Orders);
        Assert.Equal(ListingStatus.Active, (await ctx.Listings.FindAsync(listing.Id))!.Status);
    }

    [Fact]
    public async Task SubmitOffer_OnShowcase_Throws()
    {
        var (ctx, listing, buyerId) = await SeedShowcaseAsync(ListingStatus.Active);
        var service = new PriceOfferService(ctx, _messaging, _emailNotifications);

        await Assert.ThrowsAsync<ShowcaseListingException>(
            () => service.SubmitOfferAsync(listing.Id, buyerId, 80m));
        Assert.Empty(ctx.PriceOffers);
    }

    [Fact]
    public async Task SeededShowcaseOffer_CannotBeAcceptedRejectedOrExpired()
    {
        var (ctx, listing, buyerId) = await SeedShowcaseAsync(ListingStatus.Active);
        var offer = new PriceOffer
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyerId,
            SellerId = listing.SellerId,
            Amount = 80m,
            Status = PriceOfferStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        ctx.PriceOffers.Add(offer);
        await ctx.SaveChangesAsync();
        var service = new PriceOfferService(ctx, _messaging, _emailNotifications);

        await Assert.ThrowsAsync<ShowcaseListingException>(() => service.AcceptOfferAsync(offer.Id, listing.SellerId));
        await Assert.ThrowsAsync<ShowcaseListingException>(() => service.RejectOfferAsync(offer.Id, listing.SellerId));
        await service.ExpireOfferAsync(offer.Id);
        await service.CancelPendingOffersForListingAsync(listing.Id);

        Assert.Equal(PriceOfferStatus.Pending, (await ctx.PriceOffers.FindAsync(offer.Id))!.Status);
    }

    [Fact]
    public async Task SubmitBundleOffer_IncludingShowcase_Throws()
    {
        var (ctx, showcase, buyerId) = await SeedShowcaseAsync(ListingStatus.Active);
        var regular = new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = showcase.SellerId,
            CategoryId = showcase.CategoryId,
            Title = "Regular item",
            DesiredPrice = 40m,
            MinPrice = 20m,
            CurrentPrice = 40m,
            Status = ListingStatus.Active,
            SellByDate = DateTime.UtcNow.AddDays(10)
        };
        ctx.Listings.Add(regular);
        await ctx.SaveChangesAsync();
        var service = new BundleOfferService(ctx, _messaging, _emailNotifications);

        await Assert.ThrowsAsync<ShowcaseListingException>(() =>
            service.SubmitBundleOfferAsync(showcase.SellerId, buyerId, new List<Guid> { showcase.Id, regular.Id }, 100m));
        Assert.Empty(ctx.BundleOffers);
    }
}
