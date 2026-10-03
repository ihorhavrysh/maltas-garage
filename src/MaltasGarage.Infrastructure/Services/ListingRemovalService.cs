using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class ListingRemovalService : IListingRemovalService
{
    private readonly ApplicationDbContext _context;
    private readonly IImageService _images;
    private readonly IPriceOfferService _offers;

    public ListingRemovalService(ApplicationDbContext context, IImageService images, IPriceOfferService offers)
    {
        _context = context;
        _images = images;
        _offers = offers;
    }

    public async Task DeleteAsync(Guid listingId, Guid sellerId)
    {
        var listing = await _context.Listings
            .Include(l => l.Bids)
            .Include(l => l.Images)
            .FirstOrDefaultAsync(l => l.Id == listingId && l.SellerId == sellerId)
            ?? throw new InvalidOperationException("Listing not found.");

        listing.EnsureNotShowcase();

        if (listing.Bids.Count > 0)
            throw new InvalidOperationException("Cannot delete a listing that already has bids.");

        if (listing.Status == ListingStatus.Sold)
            throw new InvalidOperationException("A sold listing cannot be deleted.");

        // Offers, bundle offers and orders keep a Restrict foreign key to the listing: removing it
        // would fail with a database error. Those listings are cancelled and keep their photos
        var referenced =
            await _context.PriceOffers.AnyAsync(o => o.ListingId == listingId) ||
            await _context.BundleOfferItems.AnyAsync(i => i.ListingId == listingId) ||
            await _context.Orders.AnyAsync(o => o.ListingId == listingId) ||
            await _context.OrderItems.AnyAsync(i => i.ListingId == listingId);

        if (referenced)
        {
            listing.Status = ListingStatus.Cancelled;
            await _context.SaveChangesAsync();
            await _offers.CancelOpenOffersForListingAsync(listingId);
            return;
        }

        var urls = listing.Images.Select(i => i.Url).ToList();   // the thumbnail goes with its image
        _context.Listings.Remove(listing);   // images and favourites cascade
        await _context.SaveChangesAsync();

        // Files go only after the rows: a failed delete must not leave a listing without photos
        foreach (var url in urls)
            await _images.DeleteAsync(url);
    }
}
