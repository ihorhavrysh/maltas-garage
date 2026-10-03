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
public class ShipModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IMessagingService _messaging;
    private readonly IEmailNotificationService _emailNotifications;
    private readonly IOrderService _orderService;

    public ShipModel(ApplicationDbContext context, ICurrentUserService currentUser, IMessagingService messaging,
        IEmailNotificationService emailNotifications, IOrderService orderService)
    {
        _context = context;
        _currentUser = currentUser;
        _messaging = messaging;
        _emailNotifications = emailNotifications;
        _orderService = orderService;
    }

    public OrderViewModel? Order { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Tracking number is required")]
    public string TrackingNumber { get; set; } = string.Empty;


    public class OrderViewModel
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string BuyerName { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(Guid orderId)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        var order = await _context.Orders
            .Include(o => o.Listing).ThenInclude(l => l!.Images)
            .Include(o => o.Items)
            .Include(o => o.Buyer)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return NotFound();
        if (order.SellerId != userProfile?.Id) return Forbid();

        if (order.Status != OrderStatus.Paid)
        {
            TempData["Error"] = "Order is not ready for shipping";
            return RedirectToPage("/Orders/Details", new { orderId });
        }

        if (order.DeliveryMethod != DeliveryMethod.MaltaPost)
        {
            TempData["Error"] = "This order uses hand-to-hand delivery";
            return RedirectToPage("/Orders/Details", new { orderId });
        }

        Order = new OrderViewModel
        {
            Id = order.Id,
            Title = order.IsBundleOrder
                ? $"Bundle ({order.Items.Count} items)"
                : order.Listing?.Title ?? "Order",
            ImageUrl = order.Listing?.Images.OrderBy(i => i.SortOrder)
                .Select(i => i.ThumbnailUrl ?? i.Url).FirstOrDefault(),
            BuyerName = order.Buyer?.DisplayName ?? "Buyer"
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid orderId)
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync(orderId);
            return Page();
        }

        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        var exists = await _context.Orders.AnyAsync(o => o.Id == orderId);
        if (!exists) return NotFound();
        if (userProfile == null) return Forbid();

        // Seller, status and delivery method are all checked by the service on every POST
        Order order;
        try
        {
            order = await _orderService.MarkShippedAsync(orderId, userProfile.Id, TrackingNumber);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage("/Orders/Details", new { orderId });
        }

        var orderTitle = order.IsBundleOrder
            ? $"Bundle ({order.Items.Count} items)"
            : order.Listing?.Title ?? "Order";

        await _messaging.CreateSystemMessageAsync(
            order.BuyerId,
            SystemMessageType.OrderShipped,
            $"Your order \"{orderTitle}\" has been shipped! Tracking: {TrackingNumber}.",
            $"/Orders/Details/{orderId}");
        await _emailNotifications.NotifyOrderShippedAsync(order.BuyerId, orderTitle, TrackingNumber, orderId);

        TempData["Success"] = "Order marked as shipped! The buyer can now track their package.";
        return RedirectToPage("/Orders/Details", new { orderId });
    }
}
