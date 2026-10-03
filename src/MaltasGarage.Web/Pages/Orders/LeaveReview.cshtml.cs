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
    private readonly IReviewService _reviews;

    public LeaveReviewModel(ApplicationDbContext context, ICurrentUserService currentUser, IReviewService reviews)
    {
        _context = context;
        _currentUser = currentUser;
        _reviews = reviews;
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
            .Include(o => o.Listing).ThenInclude(l => l!.Images)
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

        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            return NotFound();

        if (order.BuyerId != userProfile.Id && order.SellerId != userProfile.Id)
            return Forbid();

        // [Range(1, 5)] only takes effect when ModelState is checked
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please choose a rating from 1 to 5.";
            return RedirectToPage(new { orderId });
        }

        // The service checks the order again (completed, not reviewed yet): the form can be
        // submitted without ever opening the page
        try
        {
            await _reviews.LeaveReviewAsync(orderId, userProfile.Id, Rating, Comment);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage("/Orders/Details", new { orderId });
        }

        TempData["Success"] = "Thank you for your review!";
        return RedirectToPage("/Orders/Details", new { orderId });
    }
}
