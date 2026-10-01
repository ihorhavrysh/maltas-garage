using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Orders;

[Authorize]
public class PayForAuctionModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public PayForAuctionModel(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public OrderViewModel? Order { get; set; }

    public class OrderViewModel
    {
        public Guid Id { get; set; }
        public Guid ListingId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string SellerName { get; set; } = string.Empty;
        public decimal FinalPrice { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid orderId)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        var order = await _context.Orders
            .Include(o => o.Listing).ThenInclude(l => l!.Images)
            .Include(o => o.Seller)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        // Auction orders always have exactly one listing; bundle orders never come here
        if (order?.Listing == null)
            return NotFound();

        if (order.BuyerId != userProfile?.Id)
            return Forbid();

        if (order.Status != OrderStatus.Pending)
        {
            TempData["Error"] = "This order has already been paid.";
            return RedirectToPage("/Orders/Details", new { orderId });
        }

        Order = new OrderViewModel
        {
            Id = order.Id,
            ListingId = order.ListingId.GetValueOrDefault(),
            Title = order.Listing.Title,
            ImageUrl = order.Listing.Images.OrderBy(i => i.SortOrder)
                .Select(i => i.ThumbnailUrl ?? i.Url).FirstOrDefault(),
            SellerName = order.Seller?.DisplayName ?? "Seller",
            FinalPrice = order.FinalPrice
        };

        return Page();
    }
}
