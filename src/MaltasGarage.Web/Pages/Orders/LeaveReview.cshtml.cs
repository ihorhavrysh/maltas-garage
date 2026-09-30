using System.ComponentModel.DataAnnotations;
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
public class LeaveReviewModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public LeaveReviewModel(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public OrderViewModel? Order { get; set; }
    public bool IsBuyer { get; set; }
    public bool AlreadyReviewed { get; set; }

    [BindProperty]
    [Range(1, 5)]
    public int Rating { get; set; } = 5;

    [BindProperty]
    [StringLength(1000)]
    public string? Comment { get; set; }

    public class OrderViewModel
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string OtherPartyName { get; set; } = string.Empty;
        public Guid OtherPartyId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid orderId)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null)
            return RedirectToPage("/Index");

        var order = await _context.Orders
            .Include(o => o.Listing).ThenInclude(l => l.Images)
            .Include(o => o.Items).ThenInclude(i => i.Listing).ThenInclude(l => l.Images)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .Include(o => o.Reviews)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            return NotFound();

        IsBuyer = order.BuyerId == userProfile.Id;
        var isSeller = order.SellerId == userProfile.Id;

        if (!IsBuyer && !isSeller)
            return Forbid();

        if (order.Status != OrderStatus.Completed)
        {
            TempData["Error"] = "Reviews can only be left after the order is completed.";
            return RedirectToPage("/Orders/Details", new { orderId });
        }

        AlreadyReviewed = order.Reviews.Any(r => r.FromUserId == userProfile.Id);

        var firstListing = order.Listing ?? order.Items.FirstOrDefault()?.Listing;
        Order = new OrderViewModel
        {
            Id = order.Id,
            Title = order.IsBundleOrder
                ? $"Bundle ({order.Items.Count} items)"
                : order.Listing?.Title ?? "Order",
            ImageUrl = firstListing?.Images.OrderBy(i => i.SortOrder)
                .Select(i => i.ThumbnailUrl ?? i.Url).FirstOrDefault(),
            OtherPartyName = IsBuyer
                ? order.Seller?.DisplayName ?? "Seller"
                : order.Buyer?.DisplayName ?? "Buyer",
            OtherPartyId = IsBuyer ? order.SellerId : order.BuyerId
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid orderId)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null)
            return RedirectToPage("/Index");

        var order = await _context.Orders
            .Include(o => o.Reviews)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            return NotFound();

        if (order.BuyerId != userProfile.Id && order.SellerId != userProfile.Id)
            return Forbid();

        if (order.Reviews.Any(r => r.FromUserId == userProfile.Id))
        {
            TempData["Error"] = "You have already reviewed this order.";
            return RedirectToPage("/Orders/Details", new { orderId });
        }

        var isBuyer = order.BuyerId == userProfile.Id;
        var toUserId = isBuyer ? order.SellerId : order.BuyerId;

        _context.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            FromUserId = userProfile.Id,
            ToUserId = toUserId,
            Rating = Rating,
            Comment = Comment,
            CreatedAt = DateTime.UtcNow
        });

        // Save review first, then recompute rating (so the new review is included in avg)
        await _context.SaveChangesAsync();

        await UpdateUserRatingAsync(toUserId);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Thank you for your review!";
        return RedirectToPage("/Orders/Details", new { orderId });
    }

    private async Task UpdateUserRatingAsync(Guid userId)
    {
        var ratings = await _context.Reviews
            .Where(r => r.ToUserId == userId)
            .Select(r => r.Rating)
            .ToListAsync();

        var user = await _context.UserProfiles.FindAsync(userId);
        if (user != null && ratings.Count > 0)
        {
            user.Rating = Math.Round((decimal)ratings.Average(), 1);
            user.TotalReviews = ratings.Count;
        }
    }
}
