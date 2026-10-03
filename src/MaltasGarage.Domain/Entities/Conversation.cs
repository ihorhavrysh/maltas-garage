using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class Conversation : BaseEntity
{
    public Guid BuyerId { get; set; }
    public Guid SellerId { get; set; }

    public UserProfile Buyer { get; set; } = null!;
    public UserProfile Seller { get; set; } = null!;
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
