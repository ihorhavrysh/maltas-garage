using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Tests.Stubs;

/// <summary>
/// No-op stub for IMessagingService — does nothing, suitable for unit tests
/// that don't care about notifications.
/// </summary>
public class NoOpMessagingService : IMessagingService
{
    public Task<Conversation> GetOrCreateConversationAsync(Guid buyerId, Guid sellerId, Guid orderId)
        => Task.FromResult(new Conversation { Id = Guid.NewGuid(), BuyerId = buyerId, SellerId = sellerId, OrderId = orderId });

    public Task SendMessageAsync(Guid conversationId, Guid fromUserId, string body)
        => Task.CompletedTask;

    public Task MarkConversationReadAsync(Guid conversationId, Guid userId)
        => Task.CompletedTask;

    public Task CreateSystemMessageAsync(Guid userId, SystemMessageType type, string body, string? link = null)
        => Task.CompletedTask;

    public Task MarkSystemMessagesReadAsync(Guid userId)
        => Task.CompletedTask;

    public Task<int> GetUnreadCountAsync(Guid userProfileId)
        => Task.FromResult(0);
}
