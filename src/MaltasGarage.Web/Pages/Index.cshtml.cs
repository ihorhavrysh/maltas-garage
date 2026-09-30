using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<CategoryViewModel> Categories { get; set; } = new();
    public List<ListingViewModel> ExpiringSoon { get; set; } = new();

    public class CategoryViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? IconClass { get; set; }
        public int ListingCount { get; set; }
    }

    public class ListingViewModel
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal CurrentPrice { get; set; }
        public decimal MinPrice { get; set; }
        public string? ImageUrl { get; set; }
        public string? City { get; set; }
        public ListingStatus Status { get; set; }
        public string TimeLeft { get; set; } = string.Empty;
        public DateTime SellByDate { get; set; }
        public DateTime? AuctionStartDate { get; set; }
        public bool IsNew { get; set; }
    }

    public async Task OnGetAsync()
    {
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

        var now = DateTime.UtcNow;
        ExpiringSoon = await _context.Listings
            .Include(l => l.Images)
            .Where(l => l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase)
            .Where(l => l.SellByDate > now)
            .OrderBy(l => l.SellByDate)
            .Take(8)
            .Select(l => new ListingViewModel
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
                TimeLeft = (l.SellByDate - now).TotalDays >= 1
                    ? $"{(int)(l.SellByDate - now).TotalDays}d left"
                    : (l.SellByDate - now).TotalHours >= 1
                        ? $"{(int)(l.SellByDate - now).TotalHours}h left"
                        : "ending soon",
                AuctionStartDate = l.AuctionStartDate,
                IsNew = l.IsNew
            })
            .ToListAsync();
    }
}
