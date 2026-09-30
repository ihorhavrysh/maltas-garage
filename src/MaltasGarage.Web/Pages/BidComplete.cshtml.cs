using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace MaltasGarage.Web.Pages;

[Authorize]
public class BidCompleteModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IBiddingService _biddingService;
    private readonly IPaymentService _paymentService;

    public BidCompleteModel(ApplicationDbContext context, ICurrentUserService currentUser,
        IBiddingService biddingService, IPaymentService paymentService)
    {
        _context = context;
        _currentUser = currentUser;
        _biddingService = biddingService;
        _paymentService = paymentService;
    }

    public async Task<IActionResult> OnGetAsync(Guid listingId, decimal amount, string payment_intent, string redirect_status)
    {
        if (redirect_status != "succeeded")
        {
            TempData["Error"] = "Payment was not completed.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        // Verify payment with Stripe
        var service = new PaymentIntentService();
        var intent = await service.GetAsync(payment_intent);
        if (intent.Status != "succeeded")
        {
            TempData["Error"] = "Payment verification failed.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null)
            return RedirectToPage("/Listing", new { id = listingId });

        // Find previous top bidder (different user) to refund if outbid
        var previousTopBid = await _context.Bids
            .Where(b => b.ListingId == listingId && b.BidderId != userProfile.Id)
            .OrderByDescending(b => b.Amount)
            .FirstOrDefaultAsync();

        // Record the bid
        var result = await _biddingService.PlaceBidAsync(listingId, userProfile.Id, amount, payment_intent);

        if (!result.Success)
        {
            // Race condition - bid no longer valid, refund the user
            await _paymentService.RefundPaymentAsync(payment_intent);
            TempData["Error"] = result.Error;
            return RedirectToPage("/Listing", new { id = listingId });
        }

        // Refund previous top bidder from different user
        if (previousTopBid?.StripePaymentIntentId != null)
            await _paymentService.RefundPaymentAsync(previousTopBid.StripePaymentIntentId);

        TempData["Success"] = $"Bid placed! You're the highest bidder at €{amount:N2}.";
        return RedirectToPage("/Listing", new { id = listingId });
    }
}
