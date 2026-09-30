using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class BundleOfferItem : BaseEntity
{
    public Guid BundleOfferId { get; set; }
    public Guid ListingId { get; set; }
    public decimal ListedPrice { get; set; }

    public BundleOffer BundleOffer { get; set; } = null!;
    public Listing Listing { get; set; } = null!;
}
