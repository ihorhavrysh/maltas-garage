using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Services;
using MaltasGarage.Tests.Stubs;

namespace MaltasGarage.Tests.Services;

public class BiddingServiceTests
{
    private static readonly NoOpMessagingService _messaging = new();
    private static readonly NoOpEmailNotificationService _emailNotifications = new();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Listing MakeListing(
        Guid sellerId,
        ListingStatus status = ListingStatus.AuctionPhase,
        decimal minPrice = 10m,
        decimal desiredPrice = 100m,
        DateTime? sellByDate = null)
    {
        return new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = sellerId,
            CategoryId = Guid.NewGuid(),
            Title = "Test Listing",
            MinPrice = minPrice,
            DesiredPrice = desiredPrice,
            CurrentPrice = minPrice,
            Status = status,
            SellByDate = sellByDate ?? DateTime.UtcNow.AddDays(7),
            AuctionStartDate = DateTime.UtcNow.AddDays(-1)
        };
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlaceBid_OwnListing_ReturnsError()
    {
        var ctx = TestDbContextFactory.Create();
        var sellerId = Guid.NewGuid();
        var listing = MakeListing(sellerId);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var service = new BiddingService(ctx, _messaging, _emailNotifications);
        var result = await service.PlaceBidAsync(listing.Id, sellerId, 11m);

        Assert.False(result.Success);
        Assert.Contains("own listing", result.Error);
    }

    [Fact]
    public async Task PlaceBid_NotAuctionPhase_ReturnsError()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = MakeListing(Guid.NewGuid(), status: ListingStatus.Active);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var service = new BiddingService(ctx, _messaging, _emailNotifications);
        var result = await service.PlaceBidAsync(listing.Id, Guid.NewGuid(), 11m);

        Assert.False(result.Success);
        Assert.Contains("not active", result.Error);
    }

    [Fact]
    public async Task PlaceBid_ExpiredAuction_ReturnsError()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = MakeListing(Guid.NewGuid(), sellByDate: DateTime.UtcNow.AddDays(-1));
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var service = new BiddingService(ctx, _messaging, _emailNotifications);
        var result = await service.PlaceBidAsync(listing.Id, Guid.NewGuid(), 11m);

        Assert.False(result.Success);
        Assert.Contains("ended", result.Error);
    }

    [Fact]
    public async Task PlaceBid_BelowMinimumFirstBid_ReturnsError()
    {
        var ctx = TestDbContextFactory.Create();
        // MinPrice = 10, so first valid bid must be >= 11 (10 + €1 increment)
        var listing = MakeListing(Guid.NewGuid(), minPrice: 10m);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var service = new BiddingService(ctx, _messaging, _emailNotifications);
        var result = await service.PlaceBidAsync(listing.Id, Guid.NewGuid(), 10m);

        Assert.False(result.Success);
        Assert.Contains("Minimum bid", result.Error);
    }

    [Fact]
    public async Task PlaceBid_MeetsDesiredPrice_ReturnsError()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = MakeListing(Guid.NewGuid(), minPrice: 10m, desiredPrice: 50m);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var service = new BiddingService(ctx, _messaging, _emailNotifications);
        var result = await service.PlaceBidAsync(listing.Id, Guid.NewGuid(), 50m);

        Assert.False(result.Success);
        Assert.Contains("Buy Now", result.Error);
    }

    [Fact]
    public async Task PlaceBid_ValidFirstBid_SucceedsAndUpdatesCurrentPrice()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = MakeListing(Guid.NewGuid(), minPrice: 10m, desiredPrice: 100m);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var listingId = listing.Id;
        var service = new BiddingService(ctx, _messaging, _emailNotifications);

        var result = await service.PlaceBidAsync(listingId, Guid.NewGuid(), 15m);

        Assert.True(result.Success);
        Assert.Equal(15m, result.NewHighBid);

        var updated = await ctx.Listings.FindAsync(listingId);
        Assert.Equal(15m, updated!.CurrentPrice);

        var bidCount = ctx.Bids.Count(b => b.ListingId == listingId);
        Assert.Equal(1, bidCount);
    }

    [Fact]
    public async Task PlaceBid_SecondBidMustExceedFirstByAtLeastOneEuro()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = MakeListing(Guid.NewGuid(), minPrice: 10m, desiredPrice: 100m);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var listingId = listing.Id;
        var service = new BiddingService(ctx, _messaging, _emailNotifications);

        // First bidder places €15
        await service.PlaceBidAsync(listingId, Guid.NewGuid(), 15m);

        // Second bidder tries €15 — must be at least €16
        var tooLow = await service.PlaceBidAsync(listingId, Guid.NewGuid(), 15m);
        Assert.False(tooLow.Success);
        Assert.Contains("Minimum bid", tooLow.Error);

        // Second bidder places €16 — should succeed
        var valid = await service.PlaceBidAsync(listingId, Guid.NewGuid(), 16m);
        Assert.True(valid.Success);
        Assert.Equal(16m, valid.NewHighBid);
    }

    [Fact]
    public async Task PlaceBid_ListingNotFound_ReturnsError()
    {
        var ctx = TestDbContextFactory.Create();
        var service = new BiddingService(ctx, _messaging, _emailNotifications);

        var result = await service.PlaceBidAsync(Guid.NewGuid(), Guid.NewGuid(), 20m);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }
}
