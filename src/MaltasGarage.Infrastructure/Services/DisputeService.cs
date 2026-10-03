using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class DisputeService : IDisputeService
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderService _orderService;
    private readonly IMessagingService _messaging;
    private readonly IEmailNotificationService _emailNotifications;

    public DisputeService(ApplicationDbContext context, IOrderService orderService, IMessagingService messaging, IEmailNotificationService emailNotifications)
    {
        _context = context;
        _orderService = orderService;
        _messaging = messaging;
        _emailNotifications = emailNotifications;
    }

    public async Task<Dispute> OpenDisputeAsync(Guid orderId, Guid openedById, DisputeReason reason, string? description)
    {
        var order = await _context.Orders
            .Include(o => o.Dispute)
            .Include(o => o.Listing)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException("Order not found.");

        if (order.BuyerId != openedById)
            throw new InvalidOperationException("Only the buyer can open a dispute.");

        if (order.Dispute != null && order.Dispute.Status != "Withdrawn")
            throw new InvalidOperationException("A dispute already exists for this order.");

        if (order.Status != OrderStatus.Paid && order.Status != OrderStatus.Shipped)
            throw new InvalidOperationException("A dispute can only be opened for orders that are Paid or Shipped.");

        Dispute dispute;
        if (order.Dispute != null)
        {
            // Reuse the withdrawn dispute record (one-to-one constraint).
            // Remove old attachments so the new submission starts clean.
            var oldAttachments = await _context.DisputeAttachments
                .Where(a => a.DisputeId == order.Dispute.Id)
                .ToListAsync();
            _context.DisputeAttachments.RemoveRange(oldAttachments);

            dispute = order.Dispute;
            dispute.OpenedById = openedById;
            dispute.Reason = reason;
            dispute.Description = description;
            dispute.Status = "Open";
            dispute.Resolution = null;
            dispute.AdminNotes = null;
            dispute.ResolvedAt = null;
            dispute.PreviousOrderStatus = order.Status;
        }
        else
        {
            dispute = new Dispute
            {
                OrderId = orderId,
                OpenedById = openedById,
                Reason = reason,
                Description = description,
                Status = "Open",
                PreviousOrderStatus = order.Status
            };
            _context.Disputes.Add(dispute);
        }

        order.Status = OrderStatus.Disputed;
        await _context.SaveChangesAsync();

        // Notify both parties
        await _messaging.CreateSystemMessageAsync(
            order.SellerId,
            SystemMessageType.DisputeOpened,
            $"A dispute has been opened for order #{order.Id.ToString()[..8]}. Our team will review it shortly.",
            $"/Orders/DisputeView/{orderId}");

        await _messaging.CreateSystemMessageAsync(
            openedById,
            SystemMessageType.DisputeOpened,
            $"Your dispute for order #{order.Id.ToString()[..8]} has been submitted. We'll be in touch.",
            $"/Orders/DisputeView/{orderId}");

        var listingTitle = order.Listing?.Title ?? "your item";
        await _emailNotifications.NotifyDisputeUpdateAsync(order.SellerId, listingTitle, "A buyer has opened a dispute on your sale. Our team will review it.", orderId);
        await _emailNotifications.NotifyDisputeUpdateAsync(openedById, listingTitle, "Your dispute has been submitted. We'll be in touch.", orderId);

        var buyerProfile  = await _context.UserProfiles.FindAsync(openedById);
        var sellerProfile = await _context.UserProfiles.FindAsync(order.SellerId);
        var reasonText = reason switch
        {
            DisputeReason.ItemNotReceived    => "Item not received",
            DisputeReason.ItemNotAsDescribed => "Item not as described",
            _                                => "Other"
        };
        await _emailNotifications.NotifyDisputeOpenedToStaffAsync(
            listingTitle, orderId,
            buyerProfile?.DisplayName ?? "Unknown",
            sellerProfile?.DisplayName ?? "Unknown",
            reasonText);

        return dispute;
    }

    public async Task MarkUnderReviewAsync(Guid orderId)
    {
        var dispute = await _context.Disputes
            .Include(d => d.Order).ThenInclude(o => o.Listing)
            .FirstOrDefaultAsync(d => d.OrderId == orderId)
            ?? throw new InvalidOperationException("Dispute not found.");

        if (dispute.Status != "Open") return;

        dispute.Status = "UnderReview";
        await _context.SaveChangesAsync();

        var listingTitle = dispute.Order.Listing?.Title ?? "your item";

        await _messaging.CreateSystemMessageAsync(
            dispute.Order.BuyerId,
            SystemMessageType.DisputeOpened,
            $"Your dispute for order #{orderId.ToString()[..8]} is now under review by our team.",
            $"/Orders/DisputeView/{orderId}");

        await _messaging.CreateSystemMessageAsync(
            dispute.Order.SellerId,
            SystemMessageType.DisputeOpened,
            $"The dispute for order #{orderId.ToString()[..8]} is now under review by our team.",
            $"/Orders/DisputeView/{orderId}");

        await _emailNotifications.NotifyDisputeUpdateAsync(dispute.Order.BuyerId, listingTitle, "Your dispute is now under review by our team.", orderId);
        await _emailNotifications.NotifyDisputeUpdateAsync(dispute.Order.SellerId, listingTitle, "The dispute for your order is now under review by our team.", orderId);
    }

    public async Task<Dispute?> GetDisputeByOrderAsync(Guid orderId)
    {
        return await _context.Disputes
            .Include(d => d.OpenedBy)
            .Include(d => d.Attachments)
            .Include(d => d.Order).ThenInclude(o => o.Listing)
            .Include(d => d.Order).ThenInclude(o => o.Buyer)
            .Include(d => d.Order).ThenInclude(o => o.Seller)
            .FirstOrDefaultAsync(d => d.OrderId == orderId);
    }

    public async Task WithdrawDisputeAsync(Guid orderId, Guid userId)
    {
        var dispute = await _context.Disputes
            .Include(d => d.Order)
            .FirstOrDefaultAsync(d => d.OrderId == orderId)
            ?? throw new InvalidOperationException("Dispute not found.");

        if (dispute.OpenedById != userId)
            throw new InvalidOperationException("Only the buyer who opened the dispute can withdraw it.");

        if (dispute.Status != "Open" && dispute.Status != "UnderReview")
            throw new InvalidOperationException("This dispute cannot be withdrawn.");

        dispute.Status = "Withdrawn";
        dispute.ResolvedAt = DateTime.UtcNow;

        // Revert order status to what it was before the dispute was opened.
        // Guard against legacy rows where PreviousOrderStatus was never set (defaults to 0 = Pending).
        var revertStatus = dispute.PreviousOrderStatus == OrderStatus.Pending
            ? OrderStatus.Paid
            : dispute.PreviousOrderStatus;
        dispute.Order.Status = revertStatus;

        await _context.SaveChangesAsync();
    }

    public async Task ResolveDisputeAsync(Guid disputeId, DisputeResolution resolution, string? adminNotes, decimal? partialRefundAmount = null)
    {
        var dispute = await _context.Disputes
            .Include(d => d.Order).ThenInclude(o => o.Payment)
            .Include(d => d.Order).ThenInclude(o => o.Listing)
            .FirstOrDefaultAsync(d => d.Id == disputeId)
            ?? throw new InvalidOperationException("Dispute not found.");

        // A dispute is resolved once. A second POST (double click, two staff members, browser
        // back) must not refund or pay out again
        if (dispute.Status != "Open" && dispute.Status != "UnderReview")
            throw new InvalidOperationException("This dispute has already been closed.");

        // Execute Stripe operation first — if it fails, dispute stays unresolved in DB
        if (resolution == DisputeResolution.RefundBuyer)
            await _orderService.RefundBuyerAsync(dispute.OrderId);
        else if (resolution == DisputeResolution.PartialRefund)
            await _orderService.RefundBuyerPartialAsync(dispute.OrderId, partialRefundAmount ?? 0m);
        else if (resolution == DisputeResolution.PaySeller)
            await _orderService.ReleaseEscrowAsync(dispute.OrderId);

        // Mark dispute as resolved only after Stripe succeeded
        dispute.Resolution = resolution;
        dispute.PartialRefundAmount = resolution == DisputeResolution.PartialRefund ? partialRefundAmount : null;
        dispute.AdminNotes = adminNotes;
        dispute.Status = "Resolved";
        dispute.ResolvedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Notify both parties
        var outcomeText = resolution switch
        {
            DisputeResolution.RefundBuyer => "The dispute has been resolved - a refund has been issued to the buyer.",
            DisputeResolution.PaySeller   => "The dispute has been resolved - payment has been released to the seller.",
            DisputeResolution.PartialRefund => "The dispute has been resolved with a partial refund.",
            _ => "The dispute has been resolved."
        };

        await _messaging.CreateSystemMessageAsync(
            dispute.Order.BuyerId,
            SystemMessageType.DisputeResolved,
            outcomeText,
            $"/Orders/Details/{dispute.OrderId}");

        await _messaging.CreateSystemMessageAsync(
            dispute.Order.SellerId,
            SystemMessageType.DisputeResolved,
            outcomeText,
            $"/Orders/Details/{dispute.OrderId}");

        var listingTitle = dispute.Order.Listing?.Title ?? "your item";
        await _emailNotifications.NotifyDisputeUpdateAsync(dispute.Order.BuyerId, listingTitle, outcomeText, dispute.OrderId);
        await _emailNotifications.NotifyDisputeUpdateAsync(dispute.Order.SellerId, listingTitle, outcomeText, dispute.OrderId);
    }
}
