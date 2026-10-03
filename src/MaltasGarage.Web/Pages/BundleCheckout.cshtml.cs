using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Domain.Exceptions;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages;

[Authorize]
public class BundleCheckoutModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPaymentService _paymentService;

    public BundleCheckoutModel(ApplicationDbContext context, ICurrentUserService currentUser, IPaymentService paymentService)
    {
        _context        = context;
        _currentUser    = currentUser;
        _paymentService = paymentService;
    }

    [BindProperty(SupportsGet = true)]
    public Guid BundleOfferId { get; set; }

    public Domain.Entities.BundleOffer? BundleOffer { get; set; }
    public string? ClientSecret { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null)
        {
            TempData["Error"] = "Admin accounts cannot make purchases.";
            return RedirectToPage("/Index");
        }

        BundleOffer = await _context.BundleOffers
            .Include(o => o.Items).ThenInclude(i => i.Listing).ThenInclude(l => l.Images)
            .Include(o => o.Seller)
            .FirstOrDefaultAsync(o => o.Id == BundleOfferId);

        if (BundleOffer == null) return NotFound();

        if (BundleOffer.BuyerId != userProfile.Id) return Forbid();

        if (BundleOffer.Items.Any(i => i.Listing.IsShowcase))
        {
            TempData["Error"] = ShowcaseListingException.DefaultMessage;
            return RedirectToPage("/Messages/Index");
        }

        if (BundleOffer.Status != BundleOfferStatus.Accepted)
        {
            TempData["Error"] = "This bundle offer is no longer valid.";
            return RedirectToPage("/Messages/Index");
        }

        if (BundleOffer.Items.Any(i => i.Listing.Status != ListingStatus.Active))
        {
            TempData["Error"] = "One or more items in this bundle are no longer available.";
            return RedirectToPage("/Messages/Index");
        }

        if (string.IsNullOrEmpty(BundleOffer.Seller?.StripeAccountId) || !BundleOffer.Seller.StripeOnboardingComplete)
        {
            TempData["Error"] = "Seller has not set up payments yet.";
            return RedirectToPage("/Messages/Index");
        }


        // The metadata lets BundlePaymentComplete check what this payment was for and who made it
        var result = await _paymentService.CreatePaymentIntentAsync(BundleOffer.OfferAmount,
            PaymentMetadata.For(PaymentMetadata.Bundle, BundleOffer.Id, userProfile.Id));
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
            return RedirectToPage("/Messages/Index");
        }

        ClientSecret = result.ClientSecret;
        return Page();
    }
}
