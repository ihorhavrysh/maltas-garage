using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using static MaltasGarage.Web.Pages.CategoryModel;

namespace MaltasGarage.Web.Pages.Categories;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private const int PageSize = 12;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<CategoryViewModel> Categories { get; set; } = new();
    public List<ListingCardViewModel> Listings { get; set; } = new();

    public string Sort { get; set; } = "newest";
    public string? Q { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Condition { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    public class CategoryViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? IconClass { get; set; }
        public int ListingCount { get; set; }
    }

    public async Task OnGetAsync(
        string? q = null,
        string sort = "newest",
        decimal? minPrice = null,
        decimal? maxPrice = null,
        string? condition = null,
        int pageNum = 1)
    {
        Sort = sort;
        Q = q?.Trim();
        MinPrice = minPrice;
        MaxPrice = maxPrice;
        Condition = condition;
        CurrentPage = pageNum;

        Categories = await _context.Categories
            .Where(c => c.ParentId == null && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryViewModel
            {
                Name = c.Name,
                Slug = c.Slug,
                IconClass = c.IconClass,
                ListingCount = c.Listings.Count(l =>
                    l.Status == ListingStatus.Active ||
                    l.Status == ListingStatus.AuctionPhase)
            })
            .ToListAsync();

        var query = _context.Listings
            .Include(l => l.Images)
            .Where(l => l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase);

        if (!string.IsNullOrEmpty(Q))
            query = query.Where(l => l.Title.Contains(Q) || (l.Description != null && l.Description.Contains(Q)));

        if (minPrice.HasValue)
            query = query.Where(l => l.CurrentPrice >= minPrice.Value);
        if (maxPrice.HasValue)
            query = query.Where(l => l.CurrentPrice <= maxPrice.Value);

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
    }
}
