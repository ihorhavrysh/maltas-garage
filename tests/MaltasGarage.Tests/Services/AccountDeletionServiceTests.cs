using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Services;

namespace MaltasGarage.Tests.Services;

public class AccountDeletionServiceTests
{
    [Fact]
    public async Task LeadingBidInSomeoneElsesAuction_BlocksDeletion()
    {
        // The leading bid is paid for: when the auction closes it would become an order for a
        // deleted account
        var ctx = TestDbContextFactory.Create();
        var bidder = Guid.NewGuid();
        var listing = new Listing
        {
            Id = Guid.NewGuid(), SellerId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), Title = "Bike",
            MinPrice = 10, DesiredPrice = 100, Status = ListingStatus.AuctionPhase, SellByDate = DateTime.UtcNow.AddDays(1)
        };
        listing.Bids.Add(new Bid { Id = Guid.NewGuid(), ListingId = listing.Id, BidderId = Guid.NewGuid(), Amount = 20 });
        listing.Bids.Add(new Bid { Id = Guid.NewGuid(), ListingId = listing.Id, BidderId = bidder, Amount = 30 });
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var service = new AccountDeletionService(ctx, null!, null!, TimeProvider.System);

        Assert.Single(await service.GetBlockersAsync(bidder));
        Assert.Empty(await service.GetBlockersAsync(listing.Bids.First(b => b.Amount == 20).BidderId));
    }

    [Fact]
    public async Task ActiveOrder_BlocksDeletion()
    {
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Shipped);

        var service = new AccountDeletionService(ctx, null!, null!, TimeProvider.System);

        Assert.NotEmpty(await service.GetBlockersAsync(order.BuyerId));
        Assert.NotEmpty(await service.GetBlockersAsync(order.SellerId));
    }
}
