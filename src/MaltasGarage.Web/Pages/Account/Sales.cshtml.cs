using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Account;

[Authorize]
public class SalesModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public SalesModel(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public UserProfile? Profile { get; set; }
    public List<Order> PendingOrders { get; set; } = [];
    public List<Order> ActiveOrders { get; set; } = [];
    public List<Order> CompletedOrders { get; set; } = [];

    public decimal TotalEarned { get; set; }
    public decimal PlatformFeesPaid { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (_currentUser.UserId == null)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        Profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (Profile == null)
            return NotFound();

        var all = await _context.Orders
            .Include(o => o.Listing)
                .ThenInclude(l => l.Images)
            .Include(o => o.Items)
            .Include(o => o.Buyer)
            .Include(o => o.Payment)
            .Where(o => o.SellerId == Profile.Id)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        PendingOrders  = all.Where(o => o.Status == OrderStatus.Pending).ToList();
        ActiveOrders   = all.Where(o => o.Status is OrderStatus.Paid
                                         or OrderStatus.Shipped
                                         or OrderStatus.Delivered
                                         or OrderStatus.Disputed).ToList();
        CompletedOrders = all.Where(o => o.Status == OrderStatus.Completed).ToList();

        TotalEarned      = CompletedOrders.Sum(o => o.SellerPayout);
        PlatformFeesPaid = CompletedOrders.Sum(o => o.PlatformFee);

        return Page();
    }
}
