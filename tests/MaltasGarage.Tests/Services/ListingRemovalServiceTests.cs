using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Services;
using MaltasGarage.Tests.Stubs;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Tests.Services;

public class ListingRemovalServiceTests
{
    private sealed class RecordingImageService : IImageService
    {
        public List<string> Deleted { get; } = new();
        public Task<ImageUploadResult> UploadAsync(Stream stream, string fileName, string folder) => throw new NotSupportedException();
        public Task DeleteAsync(string url) { Deleted.Add(url); return Task.CompletedTask; }
    }

    private static (ListingRemovalService Service, RecordingImageService Images) Create(ApplicationDbContext ctx)
    {
        var images = new RecordingImageService();
        var offers = new PriceOfferService(ctx, new NoOpMessagingService(), new NoOpEmailNotificationService(), TimeProvider.System);
        return (new ListingRemovalService(ctx, images, offers), images);
    }

    private static async Task<Listing> SeedAsync(ApplicationDbContext ctx)
    {
        var listing = new Listing
        {
            Id = Guid.NewGuid(), SellerId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), Title = "Lamp",
            MinPrice = 10, DesiredPrice = 20, Status = ListingStatus.Active, SellByDate = DateTime.UtcNow.AddDays(5)
        };
        listing.Images.Add(new ListingImage { Id = Guid.NewGuid(), ListingId = listing.Id, Url = "/uploads/listings/a.jpg" });
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();
        return listing;
    }

    [Fact]
    public async Task Delete_UnreferencedListing_RemovesItAndItsPhotos()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = await SeedAsync(ctx);
        var (service, images) = Create(ctx);

        await service.DeleteAsync(listing.Id, listing.SellerId);

        Assert.False(await ctx.Listings.AnyAsync());
        Assert.Equal("/uploads/listings/a.jpg", Assert.Single(images.Deleted));
    }

    [Fact]
    public async Task Delete_ListingWithOffers_CancelsItAndTheOffers()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = await SeedAsync(ctx);
        var offer = new PriceOffer { Id = Guid.NewGuid(), ListingId = listing.Id, BuyerId = Guid.NewGuid(), SellerId = listing.SellerId, Amount = 15, ExpiresAt = DateTime.UtcNow.AddHours(5) };
        ctx.PriceOffers.Add(offer);
        await ctx.SaveChangesAsync();
        var (service, images) = Create(ctx);

        await service.DeleteAsync(listing.Id, listing.SellerId);

        Assert.Equal(ListingStatus.Cancelled, (await ctx.Listings.FindAsync(listing.Id))!.Status);
        Assert.Equal(PriceOfferStatus.Cancelled, (await ctx.PriceOffers.FindAsync(offer.Id))!.Status);
        Assert.Empty(images.Deleted);
    }

    [Fact]
    public async Task Delete_SomeoneElsesListing_Throws()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = await SeedAsync(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(ctx).Service.DeleteAsync(listing.Id, Guid.NewGuid()));
        Assert.True(await ctx.Listings.AnyAsync());
    }
}
