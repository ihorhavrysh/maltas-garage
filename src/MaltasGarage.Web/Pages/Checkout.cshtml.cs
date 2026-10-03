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
public class CheckoutModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPaymentService _paymentService;
    private readonly IOrderService _orderService;
    private readonly IListingLifecycleService _lifecycle;

    public CheckoutModel(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IPaymentService paymentService,
        IOrderService orderService,
        IListingLifecycleService lifecycle)
    {
        _context = context;
        _currentUser = currentUser;
        _paymentService = paymentService;
        _orderService = orderService;
        _lifecycle = lifecycle;
    }

    [BindProperty(SupportsGet = true)]
    public Guid ListingId { get; set; }

    // Computed on the server (Buy Now price or this buyer's accepted offer), never bound from the URL
    public decimal Price { get; private set; }

    [BindProperty(SupportsGet = true)]
    public Guid? OfferId { get; set; }

public ListingViewModel? Listing { get; set; }
    public string? ClientSecret { get; set; }

    public class ListingViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string SellerName { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null)
        {
            TempData["Error"] = "Admin accounts cannot make purchases. Use a regular buyer account.";
            return RedirectToPage("/Listing", new { id = ListingId });
        }

        await _lifecycle.EnsureCurrentAsync(ListingId);
        var listing = await _context.Listings
            .Include(l => l.Images)
            .Include(l => l.Seller)
            .FirstOrDefaultAsync(l => l.Id == ListingId);

        if (listing == null)
            return NotFound();

        if (listing.IsShowcase)
        {
            TempData["Error"] = ShowcaseListingException.DefaultMessage;
            return RedirectToPage("/Listing", new { id = ListingId });
        }

        if (listing.Status is not (ListingStatus.Active or ListingStatus.AuctionPhase))
        {
            TempData["Error"] = listing.Status == ListingStatus.Sold
                ? "This item has already been sold."
                : "This listing is no longer available.";
            return RedirectToPage("/Listing", new { id = ListingId });
        }

        if (userProfile.Id == listing.SellerId)
        {
            TempData["Error"] = "You cannot buy your own listing.";
            return RedirectToPage("/Listing", new { id = ListingId });
        }

        if (string.IsNullOrEmpty(listing.Seller?.StripeAccountId) || !listing.Seller.StripeOnboardingComplete)
        {
            TempData["Error"] = "Seller has not set up payments yet.";
            return RedirectToPage("/Listing", new { id = ListingId });
        }

        Listing = new ListingViewModel
        {
            Title = listing.Title,
            ImageUrl = listing.Images.OrderBy(i => i.SortOrder).Select(i => i.ThumbnailUrl ?? i.Url).FirstOrDefault(),
            SellerName = listing.Seller.DisplayName ?? "Seller"
        };

        try
        {
            Price = await _orderService.GetCheckoutPriceAsync(ListingId, userProfile.Id, OfferId);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage("/Listing", new { id = ListingId });
        }

        // Create PaymentIntent - platform captures full amount, transfer to seller on escrow release.
        // The metadata lets PaymentComplete check what this payment was for and who made it.
        var paymentResult = await _paymentService.CreatePaymentIntentAsync(Price,
            PaymentMetadata.For(PaymentMetadata.BuyNow, ListingId, userProfile.Id, OfferId));

        if (!paymentResult.Success)
        {
            TempData["Error"] = paymentResult.Error;
            return RedirectToPage("/Listing", new { id = ListingId });
        }

        ClientSecret = paymentResult.ClientSecret;
        return Page();
    }
}
