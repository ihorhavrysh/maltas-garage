using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using static MaltasGarage.Web.Pages.CategoryModel;

namespace MaltasGarage.Web.Pages.Listings;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private const int PageSize = 12;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<ListingCardViewModel> Listings { get; set; } = new();
    public string Sort { get; set; } = "newest";
    public string? Q { get; set; }
    public string? Condition { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    public async Task<IActionResult> OnGetAsync(string sort = "newest", string? q = null, string? condition = null, int pageNum = 1)
    {
        Sort = sort;
        Q = q?.Trim();
        Condition = condition;
        CurrentPage = pageNum;

        var query = _context.Listings
            .Include(l => l.Images)
            .Where(l => l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase);

        if (!string.IsNullOrEmpty(Q))
            query = query.Where(l => l.Title.Contains(Q) || l.Description.Contains(Q));

        if (condition == "new")
            query = query.Where(l => l.IsNew);
        else if (condition == "used")
            query = query.Where(l => !l.IsNew);

        query = sort switch
        {
            "price-asc"  => query.OrderBy(l => l.CurrentPrice),
            "price-desc" => query.OrderByDescending(l => l.CurrentPrice),
            "ending"     => query.OrderBy(l => l.SellByDate),
            _            => query.OrderByDescending(l => l.PublishedAt)
        };

        TotalCount = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalCount / (double)PageSize);

        Listings = await query
            .Skip((pageNum - 1) * PageSize)
            .Take(PageSize)
            .Select(l => new ListingCardViewModel
            {
                Id               = l.Id,
                Title            = l.Title,
                CurrentPrice     = l.Status == ListingStatus.AuctionPhase && l.Bids.Any()
                    ? l.Bids.Max(b => b.Amount)
                    : l.CurrentPrice,
                MinPrice         = l.MinPrice,
                ImageUrl         = l.Images.OrderBy(i => i.SortOrder).Select(i => i.ThumbnailUrl ?? i.Url).FirstOrDefault(),
                City             = l.PickupCity,
                Status           = l.Status,
                SellByDate       = l.SellByDate,
                AuctionStartDate = l.AuctionStartDate,
                IsNew            = l.IsNew,
                IsShowcase       = l.IsShowcase,
                BidCount         = l.Bids.Count()
            })
            .ToListAsync();

        return Page();
    }
}
