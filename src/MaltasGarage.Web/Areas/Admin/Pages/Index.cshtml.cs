using System.Security.Claims;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Web.Areas.Admin.Pages;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly DemoResetService _demoReset;
    private readonly DemoSettings _demo;

    public IndexModel(ApplicationDbContext context, DemoResetService demoReset, IOptions<DemoSettings> demo)
    {
        _context = context;
        _demoReset = demoReset;
        _demo = demo.Value;
    }

    /// <summary>The public demo admin must not be able to wipe everyone's data, a real admin can.</summary>
    public bool CanResetDemo { get; set; }

    public int TotalUsers { get; set; }
    public int ActiveListings { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public int OpenDisputes { get; set; }
    public List<RecentOrderRow> RecentOrders { get; set; } = new();

    public class RecentOrderRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string BuyerName { get; set; } = string.Empty;
        public string SellerName { get; set; } = string.Empty;
        public decimal FinalPrice { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // Dashboard is Admin-only; Managers get redirected to Disputes
        if (!User.IsInRole("Admin"))
            return RedirectToPage("/Disputes/Index", new { area = "Admin" });

        ViewData["ActivePage"] = "Dashboard";
        CanResetDemo = await IsRealAdminInDemoAsync();

        TotalUsers = await _context.Users.CountAsync();
        ActiveListings = await _context.Listings.CountAsync(l =>
            l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase);
        TotalOrders = await _context.Orders.CountAsync();
        TotalRevenue = await _context.Orders
            .Where(o => o.Status == OrderStatus.Completed)
            .SumAsync(o => (decimal?)o.PlatformFee) ?? 0;
        OpenDisputes = await _context.Disputes
            .CountAsync(d => d.Status == "Open" || d.Status == "UnderReview");

        RecentOrders = await _context.Orders
            .Include(o => o.Listing)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .OrderByDescending(o => o.CreatedAt)
            .Take(10)
            .Select(o => new RecentOrderRow
            {
                Id = o.Id,
                Title = o.Listing != null ? o.Listing.Title : "Bundle order",
                BuyerName = o.Buyer.DisplayName ?? "-",
                SellerName = o.Seller.DisplayName ?? "-",
                FinalPrice = o.FinalPrice,
                Status = o.Status.ToString(),
                CreatedAt = o.CreatedAt
            })
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostResetDemoAsync()
    {
        if (!await IsRealAdminInDemoAsync())
            return Forbid();

        await _demoReset.ResetAsync("Manual");
        TempData["Success"] = "Demo data has been reset.";
        return RedirectToPage();
    }

    private async Task<bool> IsRealAdminInDemoAsync()
    {
        if (!_demo.Enabled || !User.IsInRole("Admin")) return false;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !await _context.UserProfiles.AnyAsync(p => p.UserId == userId && p.IsDemoAccount);
    }
}
