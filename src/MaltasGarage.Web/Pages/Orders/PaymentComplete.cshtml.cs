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
public class PaymentCompleteModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPaymentService _paymentService;
    private readonly IPurchaseCompletionService _purchases;

    public PaymentCompleteModel(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IPaymentService paymentService,
        IPurchaseCompletionService purchases)
    {
        _context = context;
        _currentUser = currentUser;
        _paymentService = paymentService;
        _purchases = purchases;
    }

    public async Task<IActionResult> OnGetAsync(
        Guid listingId,
        string deliveryMethod,
        string? payment_intent,
        string? redirect_status)
    {
        if (redirect_status != "succeeded" || string.IsNullOrEmpty(payment_intent))
        {
            TempData["Error"] = "Payment was not completed. Please try again.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (userProfile == null)
            return RedirectToPage("/Listing", new { id = listingId });

        // The amount and the offer come from Stripe: everything in this URL is the buyer's to edit.
        // A payment made for another listing or by another account is not used here.
        var payment = await _paymentService.GetSucceededPaymentAsync(payment_intent);
        if (payment == null || !payment.IsFor(PaymentMetadata.BuyNow, listingId, userProfile.Id))
        {
            TempData["Error"] = "Payment verification failed.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        DeliveryMethod? method = Enum.TryParse<DeliveryMethod>(deliveryMethod, out var parsed) ? parsed : null;

        // A refresh, or the webhook having got there first, finds the order already made
        var result = await _purchases.CompleteAsync(payment, method);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToPage("/Listing", new { id = listingId });
        }

        return RedirectToPage("/Orders/Confirmation", new { orderId = result.OrderId });
    }
}
