using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Orders;

[Authorize]
public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IOrderService _orderService;
    private readonly IMessagingService _messaging;
    private readonly IEmailNotificationService _emailNotifications;

    public DetailsModel(ApplicationDbContext context, ICurrentUserService currentUser, IOrderService orderService,
        IMessagingService messaging, IEmailNotificationService emailNotifications)
    {
        _context = context;
        _currentUser = currentUser;
        _orderService = orderService;
        _messaging = messaging;
        _emailNotifications = emailNotifications;
    }

    public OrderDetailViewModel? Order { get; set; }
    public bool IsBuyer { get; set; }
    public bool IsSeller { get; set; }
    public bool HasReviewed { get; set; }

    // Buyer can cancel if seller hasn't shipped within 3 days
    public bool CanCancelOrder =>
        IsBuyer &&
        Order?.Status == "Paid" &&
        Order?.DeliveryMethod == "MaltaPost" &&
        (Order.DisputeStatus == null || Order.DisputeStatus == "Withdrawn") &&
        Order.PaidAt.HasValue &&
        Order.PaidAt.Value < DateTime.UtcNow.AddDays(-3);

    public class OrderDetailViewModel
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public Guid SellerId { get; set; }
        public string SellerName { get; set; } = string.Empty;
        public string? PickupCity { get; set; }
        public string? PickupAddress { get; set; }
        public string BuyerName { get; set; } = string.Empty;
        public decimal FinalPrice { get; set; }
        public string Status { get; set; } = string.Empty;
        public string DeliveryMethod { get; set; } = string.Empty;
        public bool DeliveryMethodPending { get; set; }
        public string? TrackingNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool HasDispute { get; set; }
        public string? DisputeStatus { get; set; }
        public Guid? DisputeId { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? ShippedAt { get; set; }
        public decimal PlatformFee { get; set; }
        public decimal SellerPayout { get; set; }
        public decimal? DisputePartialRefundAmount { get; set; }
        public bool IsBundleOrder { get; set; }
        public List<BundleItemViewModel> Items { get; set; } = new();
    }

    public class BundleItemViewModel
    {
        public Guid ListingId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public decimal Price { get; set; }
    }

    public class StatusStep
    {
        public string Label { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public bool IsCurrent { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid orderId)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        bool isAdminOrManager = User.IsInRole("Admin") || User.IsInRole("Manager");

        if (userProfile == null && !isAdminOrManager)
            return RedirectToPage("/Index");

        var order = await _context.Orders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(o => o.Listing).ThenInclude(l => l.Images)
            .Include(o => o.Items).ThenInclude(i => i.Listing).ThenInclude(l => l.Images)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .Include(o => o.Shipment)
            .Include(o => o.Reviews)
            .Include(o => o.Dispute)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            return NotFound();

        // Admin/Manager can view any order in read-only mode
        if (userProfile != null)
        {
            IsBuyer = order.BuyerId == userProfile.Id;
            IsSeller = order.SellerId == userProfile.Id;
            HasReviewed = order.Reviews.Any(r => r.FromUserId == userProfile.Id);
        }

        if (!IsBuyer && !IsSeller && !isAdminOrManager)
            return Forbid();

        var bundleItemCount = order.IsBundleOrder ? order.Items.Count : 0;
        Order = new OrderDetailViewModel
        {
            Id = order.Id,
            Title = order.IsBundleOrder
                ? $"Bundle ({bundleItemCount} items)"
                : order.Listing?.Title ?? "Order",
            ImageUrl = order.Listing?.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
            SellerId = order.SellerId,
            SellerName = order.Seller?.DisplayName ?? "Seller",
            PickupCity = order.Listing?.PickupCity,
            PickupAddress = order.Listing?.PickupAddress,
            BuyerName = order.Buyer?.DisplayName ?? "Buyer",
            FinalPrice = order.FinalPrice,
            Status = order.Status.ToString(),
            DeliveryMethod = order.DeliveryMethod.ToString(),
            DeliveryMethodPending = order.DeliveryMethodPending,
            TrackingNumber = order.Shipment?.TrackingNumber,
            CreatedAt = order.CreatedAt,
            HasDispute = order.Dispute != null,
            DisputeStatus = order.Dispute?.Status,
            DisputeId = order.Dispute?.Id,
            PaidAt = order.PaidAt,
            ShippedAt = order.Shipment?.ShippedAt,
            PlatformFee = order.PlatformFee,
            SellerPayout = order.SellerPayout,
            DisputePartialRefundAmount = order.Dispute?.PartialRefundAmount,
            IsBundleOrder = order.IsBundleOrder,
            Items = order.Items.Select(i => new BundleItemViewModel
            {
                ListingId = i.ListingId,
                Title = i.Listing?.Title ?? "Item",
                ThumbnailUrl = i.Listing?.Images.OrderBy(img => img.SortOrder)
                    .Select(img => img.ThumbnailUrl ?? img.Url).FirstOrDefault(),
                Price = i.Price
            }).ToList()
        };

        return Page();
    }

    public async Task<IActionResult> OnPostConfirmReceiptAsync(Guid orderId)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        var order = await _context.Orders
            .Include(o => o.Listing)
            .Include(o => o.Items).ThenInclude(i => i.Listing)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return NotFound();
        if (userProfile?.Id != order.BuyerId) return Forbid();

        try
        {
            await _orderService.ReleaseEscrowAsync(orderId);
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage(new { orderId });
        }

        var orderTitle = order.IsBundleOrder
            ? $"Bundle ({order.Items.Count} items)"
            : order.Listing?.Title ?? "Order";

        await _messaging.CreateSystemMessageAsync(
            order.SellerId,
            Domain.Enums.SystemMessageType.EscrowReleased,
            $"The buyer has confirmed receipt of \"{orderTitle}\". Payment has been released to your account.",
            $"/Orders/Details/{orderId}");

        await _emailNotifications.NotifyOrderCompletedSellerAsync(order.SellerId, orderTitle, orderId);

        TempData["Success"] = "Order completed! Payment has been released to the seller.";
        return RedirectToPage(new { orderId });
    }

    public async Task<IActionResult> OnPostCancelOrderAsync(Guid orderId)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        var order = await _context.Orders
            .Include(o => o.Dispute)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return NotFound();
        if (userProfile?.Id != order.BuyerId) return Forbid();

        if (order.Status != Domain.Enums.OrderStatus.Paid ||
            order.DeliveryMethod != Domain.Enums.DeliveryMethod.MaltaPost ||
            (order.Dispute != null && order.Dispute.Status != "Withdrawn") ||
            order.PaidAt == null ||
            order.PaidAt > DateTime.UtcNow.AddDays(-3))
        {
            TempData["Error"] = "This order cannot be cancelled.";
            return RedirectToPage(new { orderId });
        }

        await _orderService.RefundBuyerAsync(orderId);

        TempData["Success"] = "Order cancelled and refund initiated. Funds will be returned to your original payment method.";
        return RedirectToPage(new { orderId });
    }

    public async Task<IActionResult> OnPostSelectDeliveryAsync(Guid orderId, string deliveryMethod)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return NotFound();
        if (userProfile?.Id != order.BuyerId) return Forbid();
        if (!order.DeliveryMethodPending) return RedirectToPage(new { orderId });

        if (!Enum.TryParse<DeliveryMethod>(deliveryMethod, out var method))
        {
            TempData["Error"] = "Please select a valid delivery method.";
            return RedirectToPage(new { orderId });
        }

        order.DeliveryMethod = method;
        order.DeliveryMethodPending = false;
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var methodLabel = method == DeliveryMethod.MaltaPost ? "MaltaPost delivery" : "hand-to-hand";
        var listing = await _context.Listings.FindAsync(order.ListingId);
        await _messaging.CreateSystemMessageAsync(
            order.SellerId,
            Domain.Enums.SystemMessageType.DeliveryMethodSelected,
            $"The buyer has chosen {methodLabel} for \"{listing?.Title ?? "the item"}\". You can now proceed.",
            $"/Orders/Details/{orderId}");

        TempData["Success"] = method == DeliveryMethod.MaltaPost
            ? "Delivery method set to MaltaPost. The seller will ship your item and provide a tracking number."
            : "Delivery method set to Hand-to-Hand. Contact the seller to arrange a meetup.";

        return RedirectToPage(new { orderId });
    }

    public string GetDisplayStatus()
    {
        if (Order == null) return string.Empty;

        if (IsBuyer)
        {
            return (Order.Status, Order.DeliveryMethod) switch
            {
                ("Paid", "MaltaPost")    => "Awaiting Shipment",
                ("Paid", "HandToHand")   => "Awaiting Meetup",
                ("Shipped", _)           => "In Transit",
                ("Completed", _)         => "Completed",
                ("Disputed", _)          => "Under Dispute",
                ("Cancelled", _)         => "Cancelled",
                ("Refunded", _)          => "Refunded",
                _                        => Order.Status
            };
        }

        if (IsSeller)
        {
            return (Order.Status, Order.DeliveryMethod) switch
            {
                ("Paid", "MaltaPost")    => "Action Required - Ship Now",
                ("Paid", "HandToHand")   => "Arrange Meetup",
                ("Shipped", _)           => "Shipped - Awaiting Confirmation",
                ("Completed", _)         => "Completed",
                ("Disputed", _)          => "Under Dispute",
                ("Cancelled", _)         => "Cancelled",
                ("Refunded", _)          => "Refunded",
                _                        => Order.Status
            };
        }

        // Admin view — raw status
        return Order.Status;
    }

    public string GetStatusBadgeClass()
    {
        return Order?.Status switch
        {
            "Completed" => "bg-primary",
            "Disputed"  => "bg-warning text-dark",
            "Cancelled" => "bg-secondary",
            "Refunded"  => "bg-secondary",
            "Shipped"   => "bg-info text-dark",
            _           => "bg-primary"
        };
    }

    public string GetStatusDescription()
    {
        if (Order == null) return string.Empty;

        if (Order.DeliveryMethodPending)
            return IsBuyer
                ? "Please select your preferred delivery method below."
                : "Waiting for the buyer to select a delivery method.";

        if (IsBuyer)
        {
            return (Order.Status, Order.DeliveryMethod) switch
            {
                ("Paid", "MaltaPost")  => "The seller will ship your item via MaltaPost. You'll be notified once it's on its way.",
                ("Paid", "HandToHand") => "Contact the seller to arrange a meetup. Confirm receipt once you have the item.",
                ("Shipped", _)         => "Your item is on its way. Confirm receipt once it arrives, or payment releases automatically after 5 days.",
                ("Completed", _)       => "Order completed. Thank you for your purchase!",
                ("Disputed", _)        => "A dispute is open on this order. Our team is reviewing the case.",
                ("Cancelled", _)       => "Order cancelled. Your refund has been initiated.",
                _                      => ""
            };
        }

        if (IsSeller)
        {
            return (Order.Status, Order.DeliveryMethod) switch
            {
                ("Paid", "MaltaPost")  => "Payment is secured in escrow. Package the item and ship via MaltaPost as soon as possible.",
                ("Paid", "HandToHand") => "Payment is secured in escrow. Contact the buyer to arrange a meetup.",
                ("Shipped", _)         => "Item shipped. Payment will be released once the buyer confirms receipt, or automatically after 5 days.",
                ("Completed", _)       => "Order completed. Payment has been released to your Stripe account.",
                ("Disputed", _)        => "A dispute has been opened on this order. Our team is reviewing the case.",
                _                      => ""
            };
        }

        // Admin view
        return Order?.Status switch
        {
            "Paid"      => "Payment received - awaiting shipment or meetup",
            "Shipped"   => "Item shipped - awaiting buyer confirmation",
            "Completed" => "Order completed",
            "Disputed"  => "Dispute in progress",
            _           => ""
        };
    }

    public List<StatusStep> GetStatusSteps()
    {
        var currentStatus = Enum.Parse<OrderStatus>(Order?.Status ?? "Pending");

        // For step display, treat Disputed as the status before dispute was opened
        if (currentStatus == OrderStatus.Disputed)
            currentStatus = Order?.DeliveryMethod == "HandToHand" ? OrderStatus.Paid : OrderStatus.Shipped;

        List<StatusStep> steps;

        if (Order?.DeliveryMethod == "HandToHand")
        {
            steps = new List<StatusStep>
            {
                new() { Label = "Payment Secured", Icon = "bi-credit-card",  IsCompleted = currentStatus >= OrderStatus.Paid },
                new() { Label = "Meetup",          Icon = "bi-person-check", IsCompleted = currentStatus >= OrderStatus.Completed },
                new() { Label = "Completed",       Icon = "bi-check-circle", IsCompleted = currentStatus >= OrderStatus.Completed }
            };
        }
        else
        {
            steps = new List<StatusStep>
            {
                new() { Label = "Payment Secured", Icon = "bi-credit-card",  IsCompleted = currentStatus >= OrderStatus.Paid },
                new() { Label = "Shipped",         Icon = "bi-truck",        IsCompleted = currentStatus >= OrderStatus.Shipped },
                new() { Label = "Completed",       Icon = "bi-check-circle", IsCompleted = currentStatus >= OrderStatus.Completed }
            };
        }

        // Mark the first uncompleted step as current
        var first = steps.FirstOrDefault(s => !s.IsCompleted);
        if (first != null) first.IsCurrent = true;

        return steps;
    }
}
