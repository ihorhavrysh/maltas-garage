using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Orders;

[Authorize]
public class BundlePaymentCompleteModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IOrderService _orderService;
    private readonly IPaymentService _paymentService;

    public BundlePaymentCompleteModel(ApplicationDbContext context, ICurrentUserService currentUser,
        IOrderService orderService, IPaymentService paymentService)
    {
        _context        = context;
        _currentUser    = currentUser;
        _orderService   = orderService;
        _paymentService = paymentService;
    }

    public async Task<IActionResult> OnGetAsync(
        Guid bundleOfferId,
        string deliveryMethod,
        string? payment_intent,
        string? redirect_status)
    {
        if (redirect_status != "succeeded" || string.IsNullOrEmpty(payment_intent))
        {
            TempData["Error"] = "Payment was not completed. Please try again.";
            return RedirectToPage("/BundleCheckout", new { bundleOfferId });
        }

        // Prevent double-processing
        var existing = await _context.Payments
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == payment_intent);
        if (existing != null)
            return RedirectToPage("/Orders/Confirmation", new { orderId = existing.OrderId });

        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (userProfile == null)
            return RedirectToPage("/Messages/Index");

        // The amount is what Stripe charged for this bundle offer, by this account; nothing from the URL
        var payment = await _paymentService.GetSucceededPaymentAsync(payment_intent);
        if (payment == null || !payment.IsFor(PaymentMetadata.Bundle, bundleOfferId, userProfile.Id))
        {
            TempData["Error"] = "Payment verification failed.";
            return RedirectToPage("/BundleCheckout", new { bundleOfferId });
        }

        var price = payment.Amount;

        if (!Enum.TryParse<DeliveryMethod>(deliveryMethod, out var method))
            method = DeliveryMethod.HandToHand;

        Domain.Entities.Order order;
        try
        {
            order = await _orderService.CreateBundleOrderAsync(bundleOfferId, userProfile.Id, price, method);
        }
        catch (Exception ex)
        {
            // A parallel return for the same payment may have created the order already; then
            // this request must not refund it
            _context.ChangeTracker.Clear();
            var listingIds = await _context.BundleOfferItems
                .Where(i => i.BundleOfferId == bundleOfferId)
                .Select(i => i.ListingId)
                .ToListAsync();
            var ownOrder = await _context.Orders
                .Where(o => o.IsBundleOrder && o.BuyerId == userProfile.Id && o.Status != OrderStatus.Refunded &&
                            o.Items.Any(i => listingIds.Contains(i.ListingId)))
                .Select(o => (Guid?)o.Id)
                .FirstOrDefaultAsync();
            if (ownOrder != null)
                return RedirectToPage("/Orders/Confirmation", new { orderId = ownOrder });

            await _paymentService.RefundPaymentAsync(payment_intent);
            TempData["Error"] = ex.Message;
            return RedirectToPage("/Messages/Index");
        }

        order.Status = OrderStatus.Paid;
        order.PaidAt = DateTime.UtcNow;

        _context.Payments.Add(new Domain.Entities.Payment
        {
            Id                    = Guid.NewGuid(),
            OrderId               = order.Id,
            StripePaymentIntentId = payment_intent,
            Amount                = price,
            Status                = PaymentStatus.Captured,
            CreatedAt             = DateTime.UtcNow,
            CapturedAt            = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return RedirectToPage("/Orders/Confirmation", new { orderId = order.Id });
    }
}
