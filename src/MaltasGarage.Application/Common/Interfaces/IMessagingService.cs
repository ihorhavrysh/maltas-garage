using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Application.Common.Interfaces;

/// <summary>
/// Chats and bell notifications. Every method saves before it returns, which also saves whatever
/// the caller has pending on the same DbContext: callers finish their own write first and notify
/// afterwards, so a failed notification never leaves their change half done.
/// </summary>
public interface IMessagingService
{
    /// <summary>The one conversation between this buyer and seller, created on first use.</summary>
    Task<Conversation> GetOrCreateConversationAsync(Guid buyerId, Guid sellerId);
    Task SendMessageAsync(Guid conversationId, Guid fromUserId, string body);
    Task MarkConversationReadAsync(Guid conversationId, Guid userId);
    Task CreateSystemMessageAsync(Guid userId, SystemMessageType type, string body, string? link = null);
    Task MarkSystemMessagesReadAsync(Guid userId);
}
