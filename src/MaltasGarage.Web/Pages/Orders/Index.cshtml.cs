using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Orders;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public IndexModel(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public UserProfile? Profile { get; set; }
    public List<OrderViewModel> DisputedOrders   { get; set; } = [];
    public List<OrderViewModel> InProgressOrders { get; set; } = [];
    public List<OrderViewModel> CompletedOrders  { get; set; } = [];
    public List<OrderViewModel> OtherOrders      { get; set; } = [];

    public bool HasAnyOrders =>
        DisputedOrders.Any() || InProgressOrders.Any() || CompletedOrders.Any() || OtherOrders.Any();

    public class OrderViewModel
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public bool IsBundleOrder { get; set; }
        public decimal FinalPrice { get; set; }
        public string Status { get; set; } = string.Empty;
        public string DeliveryMethod { get; set; } = string.Empty;
        public bool DeliveryMethodPending { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null)
            return RedirectToPage("/Index");

        Profile = userProfile;

        // Step 1: load orders with basic info + item count (no nested nav projection)
        var raw = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == userProfile.Id)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.IsBundleOrder,
                o.ListingId,
                o.FinalPrice,
                o.Status,
                o.DeliveryMethod,
                o.DeliveryMethodPending,
                o.CreatedAt,
                ItemCount    = o.Items.Count(),
                ListingTitle = o.Listing != null ? o.Listing.Title : null,
            })
            .ToListAsync();

        // Step 2: fetch thumbnail URLs for non-bundle listings in a single query
        var listingIds = raw
            .Where(o => !o.IsBundleOrder && o.ListingId.HasValue)
            .Select(o => o.ListingId!.Value)
            .ToList();

        var imagesByListing = listingIds.Count > 0
            ? await _context.ListingImages
                .AsNoTracking()
                .Where(i => listingIds.Contains(i.ListingId))
                .GroupBy(i => i.ListingId)
                .Select(g => new
                {
                    ListingId = g.Key,
                    Url = g.OrderBy(i => i.SortOrder)
                           .Select(i => i.ThumbnailUrl ?? i.Url)
                           .FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.ListingId, x => x.Url)
            : new Dictionary<Guid, string?>();

        var vms = raw.Select(o => new OrderViewModel
        {
            Id                    = o.Id,
            IsBundleOrder         = o.IsBundleOrder,
            Title                 = o.IsBundleOrder ? $"Bundle · {o.ItemCount} items" : o.ListingTitle ?? "Order",
            ImageUrl              = o.IsBundleOrder || !o.ListingId.HasValue ? null
                                    : imagesByListing.GetValueOrDefault(o.ListingId.Value),
            FinalPrice            = o.FinalPrice,
            Status                = o.Status.ToString(),
            DeliveryMethod        = o.DeliveryMethod.ToString(),
            DeliveryMethodPending = o.DeliveryMethodPending,
            CreatedAt             = o.CreatedAt
        }).ToList();

        // Sort In Progress: Paid first, then Shipped, then Delivered (by status priority), then by date
        static int InProgressOrder(string s) => s switch { "Paid" => 0, "Shipped" => 1, _ => 2 };

        DisputedOrders   = vms.Where(o => o.Status == "Disputed").ToList();
        InProgressOrders = vms.Where(o => o.Status is "Paid" or "Shipped" or "Delivered")
                              .OrderBy(o => InProgressOrder(o.Status))
                              .ThenByDescending(o => o.CreatedAt)
                              .ToList();
        CompletedOrders  = vms.Where(o => o.Status == "Completed").ToList();
        OtherOrders      = vms.Where(o => o.Status is "Pending" or "Refunded" or "Cancelled").ToList();

        return Page();
    }
}
