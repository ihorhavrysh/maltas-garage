using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data.Seed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Tests.Services;

public class ShowcaseRefreshTests
{
    private static Listing Auction(bool showcase, TimeSpan remaining) => new()
    {
        Id = Guid.NewGuid(),
        SellerId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        Title = "Auction",
        Status = ListingStatus.AuctionPhase,
        IsShowcase = showcase,
        SellByDate = DateTime.UtcNow + remaining
    };

    [Fact]
    public async Task RefreshShowcase_RollsForwardOnlyShowcaseAuctionsCloseToTheirEnd()
    {
        var ctx = TestDbContextFactory.Create();
        var endingSoon = Auction(showcase: true, TimeSpan.FromHours(20));
        var alreadyEnded = Auction(showcase: true, TimeSpan.FromHours(-5));
        var plentyLeft = Auction(showcase: true, TimeSpan.FromDays(2.5));
        var regular = Auction(showcase: false, TimeSpan.FromHours(20));
        ctx.Listings.AddRange(endingSoon, alreadyEnded, plentyLeft, regular);
        await ctx.SaveChangesAsync();
        var originalPlenty = plentyLeft.SellByDate;
        var originalRegular = regular.SellByDate;

        // RefreshShowcaseAsync only touches the database, not the Identity managers
        var seeder = new DemoDataSeeder(ctx, null!, null!, Options.Create(new DemoSettings()),
            NullLogger<DemoDataSeeder>.Instance);
        await seeder.RefreshShowcaseAsync();

        var expected = DateTime.UtcNow + DemoDataSeeder.ShowcaseRollForwardTo;
        Assert.InRange(endingSoon.SellByDate, expected.AddMinutes(-1), expected.AddMinutes(1));
        Assert.InRange(alreadyEnded.SellByDate, expected.AddMinutes(-1), expected.AddMinutes(1));
        Assert.Equal(originalPlenty, plentyLeft.SellByDate);
        Assert.Equal(originalRegular, regular.SellByDate);
    }
}
