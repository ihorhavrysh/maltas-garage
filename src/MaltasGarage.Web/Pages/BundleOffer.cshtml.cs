using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages;

[Authorize]
public class BundleOfferModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IBundleOfferService _bundleOfferService;

    public BundleOfferModel(ApplicationDbContext context, ICurrentUserService currentUser, IBundleOfferService bundleOfferService)
    {
        _context            = context;
        _currentUser        = currentUser;
        _bundleOfferService = bundleOfferService;
    }

    [BindProperty(SupportsGet = true)]
    public Guid SellerId { get; set; }

    public UserProfile? Seller { get; set; }
    public List<Listing> Listings { get; set; } = new();
    public BundleOffer? ActiveOffer { get; set; }

    [BindProperty]
    public List<Guid> SelectedListings { get; set; } = new();

    [BindProperty]
    public decimal OfferAmount { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Seller = await _context.UserProfiles.FirstOrDefaultAsync(p => p.Id == SellerId);
        if (Seller == null) return NotFound();

        var buyer = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (buyer == null) return Forbid();

        if (buyer.Id == SellerId)
        {
            TempData["Error"] = "You cannot make an offer on your own listings.";
            return RedirectToPage("/Seller", new { id = SellerId });
        }

        Listings = await _context.Listings
            .Include(l => l.Images)
            .Where(l => l.SellerId == SellerId && l.Status == ListingStatus.Active)
            .OrderBy(l => l.Title)
            .ToListAsync();

        if (Listings.Count < 2)
        {
            TempData["Error"] = "This seller doesn't have enough active listings for a bundle offer.";
            return RedirectToPage("/Seller", new { id = SellerId });
        }

        ActiveOffer = await _bundleOfferService.GetActiveBundleOfferForBuyerAsync(SellerId, buyer.Id);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var buyer = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (buyer == null) return Forbid();

        if (SelectedListings.Count < 2)
        {
            TempData["Error"] = "Please select at least 2 listings.";
            return RedirectToPage(new { sellerId = SellerId });
        }

        try
        {
            var offer = await _bundleOfferService.SubmitBundleOfferAsync(SellerId, buyer.Id, SelectedListings, OfferAmount);
            TempData["Success"] = "Your bundle offer has been sent to the seller.";
            return RedirectToPage("/Messages/Index", new { c = offer.ConversationId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage(new { sellerId = SellerId });
        }
    }
}
