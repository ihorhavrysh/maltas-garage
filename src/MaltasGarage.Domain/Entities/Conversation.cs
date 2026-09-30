using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class Conversation : BaseEntity
{
    public Guid BuyerId { get; set; }
    public Guid SellerId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? ListingId { get; set; }

    public UserProfile Buyer { get; set; } = null!;
    public UserProfile Seller { get; set; } = null!;
    public Order? Order { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
