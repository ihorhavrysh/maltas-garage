using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Orders;

[Authorize]
public class DisputeViewModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDisputeService _disputeService;
    private readonly IEmailNotificationService _emailNotifications;

    public DisputeViewModel(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IDisputeService disputeService,
        IEmailNotificationService emailNotifications)
    {
        _context = context;
        _currentUser = currentUser;
        _disputeService = disputeService;
        _emailNotifications = emailNotifications;
    }

    public Guid OrderId { get; set; }
    public Dispute? Dispute { get; set; }
    public string OrderTitle { get; set; } = string.Empty;
    public bool IsBuyer { get; set; }
    public bool IsAdminOrManager { get; set; }
    // For admin email compose - IdentityUser IDs (not shown in HTML)
    public string BuyerUserId { get; set; } = string.Empty;
    public string SellerUserId { get; set; } = string.Empty;
    public string BuyerDisplayName { get; set; } = string.Empty;
    public string SellerDisplayName { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(Guid orderId)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        bool isAdminOrManager = User.IsInRole("Admin") || User.IsInRole("Manager");

        if (userProfile == null && !isAdminOrManager)
            return RedirectToPage("/Index");

        var order = await _context.Orders
            .Include(o => o.Listing)
            .Include(o => o.Items)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return NotFound();

        bool isBuyer  = userProfile != null && order.BuyerId  == userProfile.Id;
        bool isSeller = userProfile != null && order.SellerId == userProfile.Id;

        if (!isBuyer && !isSeller && !isAdminOrManager) return Forbid();

        OrderId = orderId;
        IsBuyer = isBuyer;
        IsAdminOrManager = isAdminOrManager && !isBuyer && !isSeller;
        OrderTitle = order.IsBundleOrder
            ? $"Bundle ({order.Items.Count} items)"
            : order.Listing?.Title ?? "Order";
        Dispute = await _disputeService.GetDisputeByOrderAsync(orderId);

        if (IsAdminOrManager && order.Buyer != null && order.Seller != null)
        {
            BuyerUserId      = order.Buyer.UserId;
            SellerUserId     = order.Seller.UserId;
            BuyerDisplayName = order.Buyer.DisplayName ?? "Buyer";
            SellerDisplayName = order.Seller.DisplayName ?? "Seller";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostWithdrawAsync(Guid orderId)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null) return Forbid();

        await _disputeService.WithdrawDisputeAsync(orderId, userProfile.Id);

        TempData["Success"] = "Dispute closed. The order has returned to its previous status.";
        return RedirectToPage("Details", new { orderId });
    }

    public async Task<IActionResult> OnPostMarkUnderReviewAsync(Guid orderId)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager")) return Forbid();

        await _disputeService.MarkUnderReviewAsync(orderId);

        TempData["Success"] = "Dispute marked as Under Review. Buyer and seller have been notified.";
        return RedirectToPage(new { orderId });
    }

    public async Task<IActionResult> OnPostResolveAsync(Guid orderId, string resolution, string? adminNotes, decimal? partialRefundAmount)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager")) return Forbid();

        var dispute = await _context.Disputes.FirstOrDefaultAsync(d => d.OrderId == orderId);
        if (dispute == null) return NotFound();

        if (!Enum.TryParse<DisputeResolution>(resolution, out var res))
            return RedirectToPage(new { orderId });

        try
        {
            await _disputeService.ResolveDisputeAsync(dispute.Id, res, adminNotes, partialRefundAmount);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to resolve dispute: {ex.Message}";
            return RedirectToPage(new { orderId });
        }

        TempData["Success"] = "Dispute resolved. Both parties have been notified.";
        return RedirectToPage("Details", new { orderId });
    }

    public async Task<IActionResult> OnPostEmailPartyAsync(Guid orderId, string recipientUserId, string subject, string messageBody)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager")) return Forbid();

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(messageBody))
        {
            TempData["Error"] = "Subject and message are required.";
            return RedirectToPage(new { orderId });
        }

        await _emailNotifications.SendAdminMessageAsync(recipientUserId, subject, messageBody, orderId);

        TempData["Success"] = "Message sent successfully.";
        return RedirectToPage(new { orderId });
    }
}
