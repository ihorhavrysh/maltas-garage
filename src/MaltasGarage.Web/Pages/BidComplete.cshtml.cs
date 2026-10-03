using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages;

[Authorize]
public class BidCompleteModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPaymentService _paymentService;
    private readonly IPurchaseCompletionService _purchases;

    public BidCompleteModel(ApplicationDbContext context, ICurrentUserService currentUser,
        IPaymentService paymentService, IPurchaseCompletionService purchases)
    {
        _context = context;
        _currentUser = currentUser;
        _paymentService = paymentService;
        _purchases = purchases;
    }

    public async Task<IActionResult> OnGetAsync(Guid listingId, string payment_intent, string redirect_status)
    {
        if (redirect_status != "succeeded" || string.IsNullOrEmpty(payment_intent))
        {
            TempData["Error"] = "Payment was not completed.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null)
            return RedirectToPage("/Listing", new { id = listingId });

        // The bid is what Stripe charged, for this listing, by this account; nothing from the URL
        var payment = await _paymentService.GetSucceededPaymentAsync(payment_intent);
        if (payment == null || !payment.IsFor(PaymentMetadata.Bid, listingId, userProfile.Id))
        {
            TempData["Error"] = "Payment verification failed.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        // A refresh of this page, or the webhook, may have recorded the bid already
        var result = await _purchases.CompleteAsync(payment);
        if (!result.Succeeded)
            TempData["Error"] = result.Error;
        else
            TempData["Success"] = $"Bid placed! You're the highest bidder at €{payment.Amount:N2}.";

        return RedirectToPage("/Listing", new { id = listingId });
    }
}
