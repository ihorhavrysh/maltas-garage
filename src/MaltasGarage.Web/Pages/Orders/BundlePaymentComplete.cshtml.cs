using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Stripe;

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
        decimal price,
        string deliveryMethod,
        string? payment_intent,
        string? redirect_status)
    {
        if (redirect_status != "succeeded" || string.IsNullOrEmpty(payment_intent))
        {
            TempData["Error"] = "Payment was not completed. Please try again.";
            return RedirectToPage("/BundleCheckout", new { bundleOfferId });
        }

        var intentService = new PaymentIntentService();
        var intent = await intentService.GetAsync(payment_intent);
        if (intent.Status != "succeeded")
        {
            TempData["Error"] = "Payment verification failed.";
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

        if (!Enum.TryParse<DeliveryMethod>(deliveryMethod, out var method))
            method = DeliveryMethod.HandToHand;

        Domain.Entities.Order order;
        try
        {
            order = await _orderService.CreateBundleOrderAsync(bundleOfferId, userProfile.Id, price, method);
        }
        catch (Exception ex)
        {
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
