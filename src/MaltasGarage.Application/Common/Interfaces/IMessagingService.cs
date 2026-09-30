using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Application.Common.Interfaces;

public interface IMessagingService
{
    Task<Conversation> GetOrCreateConversationAsync(Guid buyerId, Guid sellerId, Guid orderId);
    Task SendMessageAsync(Guid conversationId, Guid fromUserId, string body);
    Task MarkConversationReadAsync(Guid conversationId, Guid userId);
    Task CreateSystemMessageAsync(Guid userId, SystemMessageType type, string body, string? link = null);
    Task MarkSystemMessagesReadAsync(Guid userId);
    Task<int> GetUnreadCountAsync(Guid userProfileId);
}
