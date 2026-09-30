using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class Bid : BaseEntity
{
    public Guid ListingId { get; set; }
    public Guid BidderId { get; set; }
    public decimal Amount { get; set; }
    public bool IsWinningBid { get; set; }
    public string? StripePaymentIntentId { get; set; }

    // Navigation properties
    public Listing Listing { get; set; } = null!;
    public UserProfile Bidder { get; set; } = null!;
}
