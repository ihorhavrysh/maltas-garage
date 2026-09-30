using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Domain.Entities;

public class PriceOffer : BaseEntity
{
    public Guid ListingId       { get; set; }
    public Guid BuyerId         { get; set; }
    public Guid SellerId        { get; set; }
    public decimal Amount       { get; set; }
    public PriceOfferStatus Status { get; set; } = PriceOfferStatus.Pending;
    public DateTime ExpiresAt   { get; set; }
    public Guid? ConversationId { get; set; }

    public Listing       Listing      { get; set; } = null!;
    public UserProfile   Buyer        { get; set; } = null!;
    public UserProfile   Seller       { get; set; } = null!;
    public Conversation? Conversation { get; set; }
}
