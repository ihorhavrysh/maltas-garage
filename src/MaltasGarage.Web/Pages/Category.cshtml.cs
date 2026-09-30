using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages;

public class CategoryModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private const int PageSize = 12;

    public CategoryModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public Category? Category { get; set; }
    public List<Category> Subcategories { get; set; } = new();
    public List<ListingCardViewModel> Listings { get; set; } = new();

    public string? SelectedSubcategory { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string Sort { get; set; } = "newest";
    public string? Q { get; set; }
    public string? Condition { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    public class ListingCardViewModel
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal CurrentPrice { get; set; }
        public decimal MinPrice { get; set; }
        public string? ImageUrl { get; set; }
        public string? City { get; set; }
        public ListingStatus Status { get; set; }
        public DateTime SellByDate { get; set; }
        public DateTime? AuctionStartDate { get; set; }
        public bool IsNew { get; set; }
        public string TimeLeft
        {
            get
            {
                var r = SellByDate - DateTime.UtcNow;
                if (r.TotalDays >= 1)  return $"{(int)r.TotalDays}d left";
                if (r.TotalHours >= 1) return $"{(int)r.TotalHours}h left";
                return "ending soon";
            }
        }
    }

    public async Task<IActionResult> OnGetAsync(
        string slug,
        string? sub,
        decimal? minPrice,
        decimal? maxPrice,
        string? q = null,
        string sort = "newest",
        string? condition = null,
        int pageNum = 1)
    {
        Category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Slug == slug);

        if (Category == null)
            return NotFound();

        // Get subcategories
        Subcategories = await _context.Categories
            .Where(c => c.ParentId == Category.Id && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        SelectedSubcategory = sub;
        MinPrice = minPrice;
        MaxPrice = maxPrice;
        Sort = sort;
        Q = q?.Trim();
        Condition = condition;
        CurrentPage = pageNum;

        // Build query
        var query = _context.Listings
            .Include(l => l.Images)
            .Where(l => l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase);

        // Filter by category or subcategory
        if (!string.IsNullOrEmpty(sub))
        {
            var subCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Slug == sub && c.ParentId == Category.Id);
            if (subCategory != null)
                query = query.Where(l => l.CategoryId == subCategory.Id);
        }
        else
        {
            var categoryIds = new List<Guid> { Category.Id };
            categoryIds.AddRange(Subcategories.Select(s => s.Id));
            query = query.Where(l => categoryIds.Contains(l.CategoryId));
        }

        // Text search
        if (!string.IsNullOrEmpty(Q))
            query = query.Where(l => l.Title.Contains(Q) || l.Description.Contains(Q));

        // Price filters
        if (minPrice.HasValue)
            query = query.Where(l => l.CurrentPrice >= minPrice.Value);
        if (maxPrice.HasValue)
            query = query.Where(l => l.CurrentPrice <= maxPrice.Value);

        // Condition filter
        if (condition == "new")
            query = query.Where(l => l.IsNew);
        else if (condition == "used")
            query = query.Where(l => !l.IsNew);

        // Sorting
        query = sort switch
        {
            "price-asc" => query.OrderBy(l => l.CurrentPrice),
            "price-desc" => query.OrderByDescending(l => l.CurrentPrice),
            "ending" => query.OrderBy(l => l.SellByDate),
            _ => query.OrderByDescending(l => l.PublishedAt)
        };

        TotalCount = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalCount / (double)PageSize);

        Listings = await query
            .Skip((pageNum - 1) * PageSize)
            .Take(PageSize)
            .Select(l => new ListingCardViewModel
            {
                Id = l.Id,
                Title = l.Title,
                CurrentPrice = l.Status == ListingStatus.AuctionPhase && l.Bids.Any()
                    ? l.Bids.Max(b => b.Amount)
                    : l.CurrentPrice,
                MinPrice = l.MinPrice,
                ImageUrl = l.Images.OrderBy(i => i.SortOrder).Select(i => i.ThumbnailUrl ?? i.Url).FirstOrDefault(),
                City = l.PickupCity,
                Status = l.Status,
                SellByDate = l.SellByDate,
                AuctionStartDate = l.AuctionStartDate,
                IsNew = l.IsNew
            })
            .ToListAsync();

        return Page();
    }
}
