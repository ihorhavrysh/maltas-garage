using MaltasGarage.Domain.Entities;

namespace MaltasGarage.Application.Common.Interfaces;

public interface IBundleOfferService
{
    Task<BundleOffer> SubmitBundleOfferAsync(Guid sellerId, Guid buyerProfileId, List<Guid> listingIds, decimal offerAmount);
    Task AcceptBundleOfferAsync(Guid bundleOfferId, Guid sellerProfileId);
    Task RejectBundleOfferAsync(Guid bundleOfferId, Guid sellerProfileId);
    Task ExpireBundleOfferAsync(Guid bundleOfferId);
    Task CancelBundleOffersForListingAsync(Guid listingId);
    Task<BundleOffer?> GetActiveBundleOfferForBuyerAsync(Guid sellerId, Guid buyerProfileId);
}
