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
    private readonly IPaymentService _paymentService;
    private readonly IPurchaseCompletionService _purchases;

    public BundlePaymentCompleteModel(ApplicationDbContext context, ICurrentUserService currentUser,
        IPaymentService paymentService, IPurchaseCompletionService purchases)
    {
        _context        = context;
        _currentUser    = currentUser;
        _paymentService = paymentService;
        _purchases      = purchases;
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

        DeliveryMethod? method = Enum.TryParse<DeliveryMethod>(deliveryMethod, out var parsed) ? parsed : null;

        var result = await _purchases.CompleteAsync(payment, method);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToPage("/Messages/Index");
        }

        return RedirectToPage("/Orders/Confirmation", new { orderId = result.OrderId });
    }
}
