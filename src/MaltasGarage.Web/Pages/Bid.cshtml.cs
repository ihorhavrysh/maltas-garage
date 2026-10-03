using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Domain.Exceptions;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Web.Pages;

[Authorize]
[EnableRateLimiting("bid")]
public class BidModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPaymentService _paymentService;
    private readonly StripeSettings _stripe;
    private readonly IListingLifecycleService _lifecycle;

    public BidModel(ApplicationDbContext context, ICurrentUserService currentUser,
        IPaymentService paymentService, IOptions<StripeSettings> stripe, IListingLifecycleService lifecycle)
    {
        _context = context;
        _currentUser = currentUser;
        _paymentService = paymentService;
        _stripe = stripe.Value;
        _lifecycle = lifecycle;
    }

    public string ListingTitle { get; set; } = string.Empty;
    public string? ListingImageUrl { get; set; }
    public string? SellerName { get; set; }
    public decimal Amount { get; set; }
    public Guid ListingId { get; set; }
    public string ClientSecret { get; set; } = string.Empty;
    public string PublishableKey => _stripe.PublishableKey;

    public async Task<IActionResult> OnGetAsync(Guid listingId, decimal amount)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null)
        {
            TempData["Error"] = "Admin accounts cannot place bids. Use a regular buyer account.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        // Opens a due auction (or closes an ended one) before a payment intent is created
        await _lifecycle.EnsureCurrentAsync(listingId);
        var listing = await _context.Listings
            .Include(l => l.Seller)
            .Include(l => l.Bids)
            .Include(l => l.Images)
            .FirstOrDefaultAsync(l => l.Id == listingId);

        if (listing == null) return NotFound();

        if (listing.IsShowcase)
        {
            TempData["Error"] = ShowcaseListingException.DefaultMessage;
            return RedirectToPage("/Listing", new { id = listingId });
        }

        if (listing.Status != ListingStatus.AuctionPhase)
        {
            TempData["Error"] = "Auction is not active.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        if (listing.SellByDate <= DateTime.UtcNow)
        {
            TempData["Error"] = "Auction has ended.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        if (listing.SellerId == userProfile.Id)
        {
            TempData["Error"] = "You cannot bid on your own listing.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        // Same minimum as BiddingService.PlaceBidAsync, so a bid it would reject is never charged
        var currentHigh = listing.Bids.Any() ? listing.Bids.Max(b => b.Amount) : listing.MinPrice;
        var minimumBid = currentHigh + BiddingService.GetBidIncrement(currentHigh);
        if (amount < minimumBid)
        {
            TempData["Error"] = $"Minimum bid is €{minimumBid:N0}.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        var topBidder = listing.Bids.OrderByDescending(b => b.Amount).FirstOrDefault();
        if (topBidder?.BidderId == userProfile.Id)
        {
            TempData["Error"] = "You are already the highest bidder.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        if (amount >= listing.DesiredPrice)
        {
            TempData["Error"] = "Bid meets or exceeds Buy Now price - consider buying directly.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        if (string.IsNullOrEmpty(listing.Seller?.StripeAccountId) || !listing.Seller.StripeOnboardingComplete)
        {
            TempData["Error"] = "Seller has not set up payments yet.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        // The metadata lets BidComplete check what this payment was for and who made it
        var (_, clientSecret) = await _paymentService.CreateBidPaymentAsync(amount,
            PaymentMetadata.For(PaymentMetadata.Bid, listingId, userProfile.Id));

        ListingTitle = listing.Title;
        ListingImageUrl = listing.Images.OrderBy(i => i.SortOrder).Select(i => i.ThumbnailUrl ?? i.Url).FirstOrDefault();
        SellerName = listing.Seller.DisplayName;
        Amount = amount;
        ListingId = listingId;
        ClientSecret = clientSecret;

        return Page();
    }
}
