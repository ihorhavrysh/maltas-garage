using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Web.Services;

public static class ListingQueries
{
    /// <summary>
    /// Listings a buyer can still act on. An auction whose end time has passed stays
    /// AuctionPhase until the sweep or a page visit closes it, but it must not be shown as live.
    /// </summary>
    public static IQueryable<Listing> OnSale(this IQueryable<Listing> listings, DateTime now) =>
        listings.Where(l => (l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase) && l.SellByDate > now);
}
