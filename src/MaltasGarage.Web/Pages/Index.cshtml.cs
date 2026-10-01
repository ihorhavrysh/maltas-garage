using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using static MaltasGarage.Web.Pages.CategoryModel;

namespace MaltasGarage.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<CategoryViewModel> Categories { get; set; } = new();
    public List<ListingCardViewModel> EndingSoon { get; set; } = new();
    public List<ListingCardViewModel> JustListed { get; set; } = new();

    public class CategoryViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? IconClass { get; set; }
        public int ListingCount { get; set; }
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
        var available = _context.Listings
            .Where(l => l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase)
            .Where(l => l.SellByDate > now);

        EndingSoon = await ToCards(available
                .Where(l => l.Status == ListingStatus.AuctionPhase)
                .OrderBy(l => l.SellByDate))
            .Take(4)
            .ToListAsync();

        var endingSoonIds = EndingSoon.Select(l => l.Id).ToList();
        JustListed = await ToCards(available
                .Where(l => !endingSoonIds.Contains(l.Id))
                .OrderByDescending(l => l.PublishedAt ?? l.CreatedAt))
            .Take(8)
            .ToListAsync();
    }

    private static IQueryable<ListingCardViewModel> ToCards(IQueryable<Listing> listings) =>
        listings.Select(l => new ListingCardViewModel
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
            IsNew = l.IsNew,
            IsShowcase = l.IsShowcase,
            BidCount = l.Bids.Count()
        });
}
