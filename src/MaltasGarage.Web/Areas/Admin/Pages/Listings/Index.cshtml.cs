using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Domain.Exceptions;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Areas.Admin.Pages.Listings;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IBundleOfferService _bundleOfferService;
    private readonly IPriceOfferService _priceOfferService;

    public IndexModel(ApplicationDbContext context, IBundleOfferService bundleOfferService, IPriceOfferService priceOfferService)
    {
        _context = context;
        _bundleOfferService = bundleOfferService;
        _priceOfferService = priceOfferService;
    }

    public List<ListingRow> Listings { get; set; } = new();
    public string Filter { get; set; } = "active";

    public class ListingRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string SellerName { get; set; } = string.Empty;
        public Guid SellerId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public async Task OnGetAsync(string filter = "active")
    {
        ViewData["ActivePage"] = "Listings";
        Filter = filter;

        var query = _context.Listings
            .Include(l => l.Seller)
            .Include(l => l.Category)
            .AsQueryable();

        query = filter switch
        {
            "sold"      => query.Where(l => l.Status == ListingStatus.Sold),
            "cancelled" => query.Where(l => l.Status == ListingStatus.Cancelled || l.Status == ListingStatus.Expired),
            "all"       => query,
            _           => query.Where(l => l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase)
        };

        Listings = await query
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new ListingRow
            {
                Id = l.Id,
                Title = l.Title,
                SellerName = l.Seller.DisplayName ?? "-",
                SellerId = l.SellerId,
                CategoryName = l.Category.Name,
                Price = l.CurrentPrice,
                Status = l.Status.ToString(),
                CreatedAt = l.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid listingId)
    {
        var listing = await _context.Listings.FindAsync(listingId);
        if (listing?.IsShowcase == true)
        {
            TempData["Error"] = ShowcaseListingException.DefaultMessage;
            return RedirectToPage();
        }

        if (listing != null)
        {
            listing.Status = ListingStatus.Cancelled;
            await _context.SaveChangesAsync();
            await _priceOfferService.CancelPendingOffersForListingAsync(listingId);
            await _bundleOfferService.CancelBundleOffersForListingAsync(listingId);
        }
        TempData["Success"] = "Listing removed.";
        return RedirectToPage();
    }
}
