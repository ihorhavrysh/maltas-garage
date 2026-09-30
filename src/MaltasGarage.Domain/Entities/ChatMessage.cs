using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class ChatMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Guid FromUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public bool IsSystemNote { get; set; }
    public Guid? OrderRef       { get; set; }
    public Guid? OfferRef       { get; set; }
    public Guid? BundleOfferRef { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public UserProfile FromUser { get; set; } = null!;
}
