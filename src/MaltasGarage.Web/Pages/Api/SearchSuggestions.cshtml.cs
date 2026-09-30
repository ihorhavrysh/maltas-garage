using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Api;

public class SearchSuggestionsModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public SearchSuggestionsModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> OnGetAsync(string? q, Guid? categoryId)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3)
            return new JsonResult(Array.Empty<object>());

        q = q.Trim();

        var query = _context.Listings
            .Where(l => l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase)
            .Where(l => l.Title.Contains(q));

        if (categoryId.HasValue)
            query = query.Where(l => l.CategoryId == categoryId.Value);

        var results = await query
            .OrderByDescending(l => l.PublishedAt)
            .Take(6)
            .Select(l => new
            {
                id       = l.Id,
                title    = l.Title,
                price    = l.CurrentPrice,
                category = l.Category!.Name,
                imageUrl = l.Images.OrderBy(i => i.SortOrder)
                             .Select(i => i.ThumbnailUrl ?? i.Url)
                             .FirstOrDefault()
            })
            .ToListAsync();

        return new JsonResult(results);
    }
}
