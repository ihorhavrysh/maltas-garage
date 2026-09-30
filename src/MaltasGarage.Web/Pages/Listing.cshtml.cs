using System.Text.Json;
using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Exceptions;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages;

public class ListingModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPriceOfferService _priceOfferService;
    private readonly IListingLifecycleService _lifecycle;
    private readonly SiteInfo _site;

    public ListingModel(ApplicationDbContext context, ICurrentUserService currentUser,
        IPriceOfferService priceOfferService, IListingLifecycleService lifecycle, SiteInfo site)
    {
        _site = site;
        _context = context;
        _currentUser = currentUser;
        _priceOfferService = priceOfferService;
        _lifecycle = lifecycle;
    }

    public Listing? Listing { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public string? MetaDescription { get; set; }
    public string? JsonLd { get; set; }
    public bool IsOwner { get; set; }
    public bool HasProfile { get; set; }
    public bool IsFavourited { get; set; }
    public bool IsTopBidder { get; set; }
    public decimal? CurrentBid { get; set; }
    public int BidCount { get; set; }
    public string TimeLeft { get; set; } = string.Empty;
    public PriceOffer? ActiveOffer { get; set; }
    public bool SellerHasMoreListings { get; set; }

    [BindProperty]
    public decimal OfferAmount { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        // Evaluate on read: an auction past its end time is closed before anyone sees it
        await _lifecycle.EnsureCurrentAsync(id);

        Listing = await _context.Listings
            .Include(l => l.Category)
            .Include(l => l.Seller)
            .Include(l => l.Images)
            .Include(l => l.Bids).ThenInclude(b => b.Bidder)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (Listing == null)
            return NotFound();

        PrimaryImageUrl = Listing.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault();

        var rawDesc = Listing.Description ?? "";
        MetaDescription = rawDesc.Length > 155
            ? rawDesc[..155].TrimEnd() + "\u2026"
            : rawDesc.Length > 0
                ? rawDesc
                : $"Buy {Listing.Title} in {Listing.PickupCity ?? "Malta"}, Malta. Listed on Malta's Garage for \u20ac{Listing.DesiredPrice:N0}.";

        var imageUrl = _site.Absolute(PrimaryImageUrl ?? "/favicon.svg");
        JsonLd = $"{{\"@context\":\"https://schema.org\",\"@type\":\"Product\","
               + $"\"name\":{JsonSerializer.Serialize(Listing.Title)},"
               + $"\"description\":{JsonSerializer.Serialize(MetaDescription)},"
               + $"\"image\":{JsonSerializer.Serialize(imageUrl)},"
               + $"\"offers\":{{\"@type\":\"Offer\",\"priceCurrency\":\"EUR\","
               + $"\"price\":\"{Listing.DesiredPrice:N0}\","
               + $"\"itemCondition\":\"https://schema.org/UsedCondition\","
               + $"\"availability\":\"https://schema.org/InStock\","
               + $"\"url\":\"{_site.Absolute($"/Listing/{Listing.Id}")}\"}}}}";

        // Atomic increment: does not touch the concurrency stamp or race with other writers
        await _context.Listings.Where(l => l.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ViewCount, l => l.ViewCount + 1));

        UserProfile? userProfile = null;
        if (_currentUser.UserId != null)
        {
            userProfile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
            HasProfile = userProfile != null;
            IsOwner = userProfile?.Id == Listing.SellerId;
            if (userProfile != null)
            {
                IsFavourited = await _context.Favourites
                    .AnyAsync(f => f.UserId == userProfile.Id && f.ListingId == Listing.Id);

                if (Listing.Bids.Any())
                {
                    var topBid = Listing.Bids.OrderByDescending(b => b.Amount).First();
                    IsTopBidder = topBid.BidderId == userProfile.Id;
                }
            }
        }

        if (Listing.Bids.Any())
        {
            CurrentBid = Listing.Bids.Max(b => b.Amount);
            BidCount = Listing.Bids.Count;
        }
        else
        {
            CurrentBid = Listing.MinPrice;
        }

        if (Listing.Status == ListingStatus.Active && !IsOwner)
        {
            SellerHasMoreListings = await _context.Listings
                .CountAsync(l => l.SellerId == Listing.SellerId && l.Status == ListingStatus.Active) >= 2;

            if (userProfile != null)
                ActiveOffer = await _priceOfferService.GetActiveOfferForBuyerAsync(Listing.Id, userProfile.Id);
        }

        var remaining = Listing.SellByDate - DateTime.UtcNow;
        TimeLeft = remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}d left"
            : remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h left"
                : "ending soon";

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        if (_currentUser.UserId == null)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null) return NotFound();

        var listing = await _context.Listings
            .Include(l => l.Bids)
            .FirstOrDefaultAsync(l => l.Id == id && l.SellerId == userProfile.Id);

        if (listing == null) return NotFound();

        if (listing.IsShowcase)
        {
            TempData["Error"] = ShowcaseListingException.DefaultMessage;
            return RedirectToPage(new { id });
        }

        if (listing.Bids.Any())
        {
            TempData["Error"] = "Cannot delete a listing that already has bids.";
            return RedirectToPage(new { id });
        }

        _context.Listings.Remove(listing);
        await _context.SaveChangesAsync();

        return RedirectToPage("/Account/MyListings");
    }

    public async Task<IActionResult> OnPostMakeOfferAsync(Guid id)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        var buyerProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (buyerProfile == null)
        {
            TempData["Error"] = "Admin accounts cannot make offers.";
            return RedirectToPage(new { id });
        }

        try
        {
            await _priceOfferService.SubmitOfferAsync(id, buyerProfile.Id, OfferAmount);
            TempData["Success"] = $"Your offer of €{OfferAmount:N0} has been sent to the seller. You'll be notified within 24 hours.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostBuyAtOfferPriceAsync(Guid id, Guid offerId)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        var buyerProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (buyerProfile == null) return Forbid();

        var offer = await _context.PriceOffers
            .FirstOrDefaultAsync(o => o.Id == offerId && o.BuyerId == buyerProfile.Id);

        if (offer == null || offer.Status != PriceOfferStatus.Accepted)
        {
            TempData["Error"] = "This offer is no longer valid.";
            return RedirectToPage(new { id });
        }

        await _lifecycle.EnsureCurrentAsync(id);
        var listing = await _context.Listings.FindAsync(id);
        if (listing?.IsShowcase == true)
        {
            TempData["Error"] = ShowcaseListingException.DefaultMessage;
            return RedirectToPage(new { id });
        }

        if (listing == null || listing.Status != ListingStatus.Active)
        {
            TempData["Error"] = "This listing is no longer available.";
            return RedirectToPage(new { id });
        }

        return RedirectToPage("/Checkout", new { listingId = id, price = offer.Amount, offerId = offer.Id });
    }

    public async Task<IActionResult> OnPostBuyNowAsync(Guid id)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        var buyerProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (buyerProfile == null)
        {
            TempData["Error"] = "Admin accounts cannot purchase items. Use a regular buyer account.";
            return RedirectToPage(new { id });
        }

        // Buy now must never beat an auction that has already ended
        await _lifecycle.EnsureCurrentAsync(id);
        var listing = await _context.Listings
            .FirstOrDefaultAsync(l => l.Id == id);

        if (listing == null)
            return NotFound();

        if (listing.IsShowcase)
        {
            TempData["Error"] = ShowcaseListingException.DefaultMessage;
            return RedirectToPage(new { id });
        }

        if (listing.Status != ListingStatus.Active && listing.Status != ListingStatus.AuctionPhase)
        {
            TempData["Error"] = "This listing is no longer available";
            return RedirectToPage(new { id });
        }

        // Cancel any pending offers from other buyers
        await _priceOfferService.CancelPendingOffersForListingAsync(id);

        return RedirectToPage("/Checkout", new { listingId = id, price = listing.DesiredPrice });
    }
}
