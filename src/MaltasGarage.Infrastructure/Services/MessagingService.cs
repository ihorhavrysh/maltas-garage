using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class MessagingService : IMessagingService
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailNotificationService _emailNotifications;

    public MessagingService(ApplicationDbContext context, IEmailNotificationService emailNotifications)
    {
        _context = context;
        _emailNotifications = emailNotifications;
    }

    public async Task<Conversation> GetOrCreateConversationAsync(Guid buyerId, Guid sellerId)
    {
        var conversation = await _context.Conversations
            .FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.SellerId == sellerId);

        if (conversation == null)
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                BuyerId = buyerId,
                SellerId = sellerId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Conversations.Add(conversation);
            await _context.SaveChangesAsync();
        }

        return conversation;
    }

    public async Task SendMessageAsync(Guid conversationId, Guid fromUserId, string body)
    {
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            FromUserId = fromUserId,
            Body = body,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.ChatMessages.Add(message);

        // Touch conversation's UpdatedAt so it sorts to top
        var conversation = await _context.Conversations
            .Include(c => c.Buyer)
            .Include(c => c.Seller)
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation != null)
            conversation.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Email only once the message is stored: a failed save must not announce a message nobody can read
        if (conversation != null)
        {
            var recipientId = conversation.BuyerId == fromUserId ? conversation.SellerId : conversation.BuyerId;
            var senderName = fromUserId == conversation.BuyerId
                ? (conversation.Buyer?.DisplayName ?? "Someone")
                : (conversation.Seller?.DisplayName ?? "Someone");
            var preview = body.Length > 80 ? body[..80] + "…" : body;
            await _emailNotifications.NotifyNewChatMessageAsync(recipientId, senderName, preview, conversationId);
        }
    }

    public async Task MarkConversationReadAsync(Guid conversationId, Guid userId)
    {
        var unread = await _context.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.FromUserId != userId && !m.IsRead)
            .ToListAsync();

        foreach (var m in unread)
            m.IsRead = true;

        if (unread.Count > 0)
            await _context.SaveChangesAsync();
    }

    public async Task CreateSystemMessageAsync(Guid userId, SystemMessageType type, string body, string? link = null)
    {
        var msg = new SystemMessage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Body = body,
            Link = link,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.SystemMessages.Add(msg);
        await _context.SaveChangesAsync();
    }

    public async Task MarkSystemMessagesReadAsync(Guid userId)
    {
        var unread = await _context.SystemMessages
            .Where(m => m.UserId == userId && !m.IsRead)
            .ToListAsync();

        foreach (var m in unread)
            m.IsRead = true;

        if (unread.Count > 0)
            await _context.SaveChangesAsync();
    }
}
