using MaltasGarage.Domain.Entities;

namespace MaltasGarage.Application.Common.Interfaces;

public interface IPriceOfferService
{
    Task<PriceOffer> SubmitOfferAsync(Guid listingId, Guid buyerProfileId, decimal amount);
    Task AcceptOfferAsync(Guid offerId, Guid sellerProfileId);
    Task RejectOfferAsync(Guid offerId, Guid sellerProfileId);
    Task ExpireOfferAsync(Guid offerId);
    Task CancelPendingOffersForListingAsync(Guid listingId);
    Task<PriceOffer?> GetActiveOfferForBuyerAsync(Guid listingId, Guid buyerProfileId);
}
