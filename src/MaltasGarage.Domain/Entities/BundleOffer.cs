using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Domain.Entities;

public class BundleOffer : BaseEntity
{
    public Guid BuyerId { get; set; }
    public Guid SellerId { get; set; }
    public decimal OfferAmount { get; set; }
    public decimal TotalListedPrice { get; set; }
    public BundleOfferStatus Status { get; set; } = BundleOfferStatus.Pending;
    public DateTime ExpiresAt { get; set; }
    public Guid? ConversationId { get; set; }

    public UserProfile Buyer { get; set; } = null!;
    public UserProfile Seller { get; set; } = null!;
    public Conversation? Conversation { get; set; }
    public ICollection<BundleOfferItem> Items { get; set; } = new List<BundleOfferItem>();
}
