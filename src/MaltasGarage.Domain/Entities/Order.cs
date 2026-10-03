using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Domain.Entities;

public class Order : BaseEntity, IConcurrencyStamped
{
    public Guid ConcurrencyStamp { get; set; }

    public Guid? ListingId { get; set; }   // null for bundle orders
    public bool IsBundleOrder { get; set; }
    public Guid BuyerId { get; set; }
    public Guid SellerId { get; set; }

    public decimal FinalPrice { get; set; }
    public decimal PlatformFee { get; set; } // 10%, at least EUR 1 (PlatformFee.Calculate)
    public decimal SellerPayout { get; set; } // FinalPrice - PlatformFee

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DeliveryMethod DeliveryMethod { get; set; }
    // True for auction orders until buyer explicitly selects delivery method
    public bool DeliveryMethodPending { get; set; }

    public DateTime? PaidAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public Listing? Listing { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public UserProfile Buyer { get; set; } = null!;
    public UserProfile Seller { get; set; } = null!;
    public Payment? Payment { get; set; }
    public Shipment? Shipment { get; set; }
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public Dispute? Dispute { get; set; }
}
