using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace MaltasGarage.Web.Pages;

public class SitemapModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly SiteInfo _site;

    public SitemapModel(ApplicationDbContext context, SiteInfo site)
    {
        _context = context;
        _site = site;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var categories = await _context.Categories
            .Where(c => c.IsActive && c.ParentId == null)
            .OrderBy(c => c.SortOrder)
            .Select(c => c.Slug)
            .ToListAsync();

        var listings = await _context.Listings
            .OnSale(DateTime.UtcNow)
            .Select(l => new { l.Id, LastMod = l.UpdatedAt ?? l.CreatedAt })
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        void AddUrl(string loc, string changefreq, string priority, string? lastmod = null)
        {
            sb.Append($"  <url><loc>{loc}</loc>");
            if (lastmod != null) sb.Append($"<lastmod>{lastmod}</lastmod>");
            sb.AppendLine($"<changefreq>{changefreq}</changefreq><priority>{priority}</priority></url>");
        }

        AddUrl(_site.Absolute("/"),          "daily",   "1.0");
        AddUrl(_site.Absolute("/Listings"),   "hourly",  "0.9");
        AddUrl(_site.Absolute("/HowItWorks"), "monthly", "0.7");
        AddUrl(_site.Absolute("/Help"),       "monthly", "0.6");
        AddUrl(_site.Absolute("/Safety"),     "monthly", "0.6");
        AddUrl(_site.Absolute("/Contact"),    "monthly", "0.5");

        foreach (var slug in categories)
            AddUrl(_site.Absolute($"/Category/{slug}"), "daily", "0.8");

        foreach (var l in listings)
            AddUrl(_site.Absolute($"/Listing/{l.Id}"), "daily", "0.7", l.LastMod.ToString("yyyy-MM-dd"));

        sb.AppendLine("</urlset>");

        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }
}
