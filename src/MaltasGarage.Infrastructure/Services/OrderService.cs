using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IMessagingService _messaging;
    private readonly IEmailNotificationService _emailNotifications;
    private readonly IPaymentService _payment;

    public OrderService(ApplicationDbContext context, IMessagingService messaging,
        IEmailNotificationService emailNotifications, IPaymentService payment)
    {
        _context = context;
        _messaging = messaging;
        _emailNotifications = emailNotifications;
        _payment = payment;
    }

    public async Task<decimal> GetCheckoutPriceAsync(Guid listingId, Guid buyerId, Guid? offerId)
    {
        if (offerId == null)
        {
            var listing = await _context.Listings.AsNoTracking().FirstOrDefaultAsync(l => l.Id == listingId)
                ?? throw new InvalidOperationException("Listing not found.");
            return listing.DesiredPrice;
        }

        var offer = await _context.PriceOffers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerId);
        if (offer == null || offer.ListingId != listingId || offer.BuyerId != buyerId ||
            offer.Status != PriceOfferStatus.Accepted)
            throw new InvalidOperationException("This offer is no longer valid.");

        return offer.Amount;
    }

    public async Task<Order> CreateOrderAsync(Guid listingId, Guid buyerId, decimal price, DeliveryMethod deliveryMethod)
    {
        var listing = await _context.Listings
            .FirstOrDefaultAsync(l => l.Id == listingId);

        if (listing == null)
            throw new Exception("Listing not found");

        listing.EnsureNotShowcase();

        if (listing.Status == ListingStatus.Sold)
            throw new Exception("Listing already sold");

        var platformFee = CalculatePlatformFee(price);
        var sellerPayout = price - platformFee;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            ListingId = listingId,
            BuyerId = buyerId,
            SellerId = listing.SellerId,
            FinalPrice = price,
            PlatformFee = platformFee,
            SellerPayout = sellerPayout,
            DeliveryMethod = deliveryMethod,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Update listing status
        listing.Status = ListingStatus.Sold;
        listing.CurrentPrice = price;

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await _messaging.CreateSystemMessageAsync(
            order.SellerId,
            Domain.Enums.SystemMessageType.ItemPurchased,
            $"Your item \"{listing.Title}\" has been purchased for €{price:N0}.",
            $"/Orders/Details/{order.Id}");

        // Transactional: always send order confirmation to buyer
        await _emailNotifications.SendOrderConfirmationAsync(order.BuyerId, listing.Title, price, order.Id);
        // Preference-based: notify seller
        await _emailNotifications.NotifyNewOrderAsync(order.SellerId, listing.Title, price, order.Id);

        return order;
    }

    public async Task<Order> CreateBundleOrderAsync(Guid bundleOfferId, Guid buyerId, decimal totalPrice, DeliveryMethod deliveryMethod)
    {
        var bundleOffer = await _context.BundleOffers
            .Include(o => o.Items).ThenInclude(i => i.Listing)
            .FirstOrDefaultAsync(o => o.Id == bundleOfferId)
            ?? throw new Exception("Bundle offer not found.");

        if (bundleOffer.Status != Domain.Enums.BundleOfferStatus.Accepted)
            throw new Exception("Bundle offer is not accepted.");

        var listings = bundleOffer.Items.Select(i => i.Listing).ToList();
        listings.ForEach(l => l.EnsureNotShowcase());

        if (listings.Any(l => l.Status != ListingStatus.Active))
            throw new Exception("One or more listings are no longer available.");

        var platformFee  = CalculatePlatformFee(totalPrice);
        var sellerPayout = totalPrice - platformFee;

        var order = new Order
        {
            Id             = Guid.NewGuid(),
            IsBundleOrder  = true,
            BuyerId        = buyerId,
            SellerId       = bundleOffer.SellerId,
            FinalPrice     = totalPrice,
            PlatformFee    = platformFee,
            SellerPayout   = sellerPayout,
            DeliveryMethod = deliveryMethod,
            Status         = OrderStatus.Pending,
            CreatedAt      = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        foreach (var item in bundleOffer.Items)
        {
            _context.OrderItems.Add(new OrderItem
            {
                Id        = Guid.NewGuid(),
                OrderId   = order.Id,
                ListingId = item.ListingId,
                Price     = item.ListedPrice,
                CreatedAt = DateTime.UtcNow
            });

            item.Listing.Status       = ListingStatus.Sold;
            item.Listing.CurrentPrice = item.ListedPrice;
        }

        bundleOffer.Status    = Domain.Enums.BundleOfferStatus.Completed;
        bundleOffer.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var itemTitles = string.Join(", ", listings.Take(2).Select(l => $"\"{l.Title}\""));
        if (listings.Count > 2) itemTitles += $" and {listings.Count - 2} more";

        await _messaging.CreateSystemMessageAsync(
            order.SellerId,
            Domain.Enums.SystemMessageType.ItemPurchased,
            $"Bundle purchase: {itemTitles} - €{totalPrice:N0} total.",
            $"/Orders/Details/{order.Id}");

        await _emailNotifications.SendOrderConfirmationAsync(order.BuyerId, $"Bundle ({listings.Count} items)", totalPrice, order.Id);
        await _emailNotifications.NotifyNewOrderAsync(order.SellerId, $"Bundle ({listings.Count} items)", totalPrice, order.Id);

        return order;
    }

    public async Task<Order?> GetOrderAsync(Guid orderId)
    {
        return await _context.Orders
            .Include(o => o.Listing).ThenInclude(l => l!.Images)
            .Include(o => o.Items).ThenInclude(i => i.Listing).ThenInclude(l => l.Images)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .Include(o => o.Payment)
            .Include(o => o.Shipment)
            .FirstOrDefaultAsync(o => o.Id == orderId);
    }

    public async Task<List<Order>> GetBuyerOrdersAsync(Guid buyerId)
    {
        return await _context.Orders
            .Include(o => o.Listing).ThenInclude(l => l!.Images)
            .Include(o => o.Items).ThenInclude(i => i.Listing)
            .Include(o => o.Seller)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetSellerOrdersAsync(Guid sellerId)
    {
        return await _context.Orders
            .Include(o => o.Listing).ThenInclude(l => l!.Images)
            .Include(o => o.Items).ThenInclude(i => i.Listing)
            .Include(o => o.Buyer)
            .Where(o => o.SellerId == sellerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task UpdateStatusAsync(Guid orderId, OrderStatus status)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null)
            throw new Exception("Order not found");

        order.Status = status;
        order.UpdatedAt = DateTime.UtcNow;

        if (status == OrderStatus.Completed)
            order.CompletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task ReleaseEscrowAsync(Guid orderId)
    {
        var order = await _context.Orders
            .Include(o => o.Payment)
            .Include(o => o.Seller)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new Exception("Order not found");

        if (order.Status == OrderStatus.Completed || order.Status == OrderStatus.Refunded)
            throw new Exception("Order already resolved");

        // Escrow can only be released once the buyer has paid: Paid (hand-to-hand),
        // Shipped/Delivered (MaltaPost) or Disputed (resolved in favour of the seller).
        if (order.Status is not (OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Disputed))
            throw new Exception($"Cannot release escrow for order {orderId}: order status is {order.Status}");

        if (order.Payment == null)
            throw new Exception($"Cannot release escrow for order {orderId}: Payment is null");

        // No PaymentIntent means no money went through Stripe (seeded demo orders), so there is
        // nothing to transfer: release in the database only, as RefundBuyerAsync and
        // AutoReleaseDueEscrowAsync already do
        if (order.Payment.StripePaymentIntentId != null)
        {
            if (order.Seller?.StripeAccountId == null)
                throw new Exception($"Cannot release escrow for order {orderId}: Seller StripeAccountId is null");
            if (order.Payment.Status != PaymentStatus.Captured)
                throw new Exception($"Cannot release escrow for order {orderId}: Payment.Status is {order.Payment.Status} (expected Captured)");

            // Transfer seller's payout — this is when money actually moves from platform to seller
            var transferred = await _payment.CreateTransferAsync(
                order.SellerPayout,
                order.Seller.StripeAccountId,
                orderId.ToString(),
                order.Payment.StripePaymentIntentId);

            if (!transferred)
                throw new Exception($"Stripe transfer failed for order {orderId}.");
        }

        order.Payment.ReleasedAt = DateTime.UtcNow;

        order.Status = OrderStatus.Completed;
        order.CompletedAt = DateTime.UtcNow;

        if (order.Payment != null)
            order.Payment.Status = PaymentStatus.Released;

        var sellerProfile = order.Seller;
        if (sellerProfile != null)
            sellerProfile.TotalSales++;

        await _context.SaveChangesAsync();
    }

    public static readonly TimeSpan MaltaPostAutoRelease = TimeSpan.FromDays(5);
    public static readonly TimeSpan HandToHandAutoRelease = TimeSpan.FromDays(7);

    public async Task<int> AutoReleaseDueEscrowAsync(DateTime now)
    {
        var maltaPostCutoff = now - MaltaPostAutoRelease;
        var handToHandCutoff = now - HandToHandAutoRelease;

        var dueIds = await _context.Orders
            .Where(o => o.Payment != null && o.Payment.Status == PaymentStatus.Captured)
            .Where(o =>
                (o.Status == OrderStatus.Shipped && o.DeliveryMethod == DeliveryMethod.MaltaPost &&
                 o.Shipment != null && o.Shipment.ShippedAt < maltaPostCutoff) ||
                (o.Status == OrderStatus.Paid && o.DeliveryMethod == DeliveryMethod.HandToHand &&
                 o.PaidAt != null && o.PaidAt < handToHandCutoff))
            .Select(o => o.Id)
            .ToListAsync();

        var released = 0;
        foreach (var orderId in dueIds)
        {
            var order = await _context.Orders
                .Include(o => o.Payment)
                .Include(o => o.Seller)
                .Include(o => o.Listing)
                .FirstAsync(o => o.Id == orderId);

            // Transfer first: it is idempotent per order, so a retry or a race is safe
            if (order.Payment!.StripePaymentIntentId != null && order.Seller.StripeAccountId != null)
            {
                if (!await _payment.CreateTransferAsync(order.SellerPayout, order.Seller.StripeAccountId,
                        order.Id.ToString(), order.Payment.StripePaymentIntentId))
                    continue;
            }

            order.Status = OrderStatus.Completed;
            order.CompletedAt = now;
            order.Payment.Status = PaymentStatus.Released;
            order.Payment.ReleasedAt = now;
            order.Seller.TotalSales++;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Released at the same moment by the buyer or another sweep
                _context.ChangeTracker.Clear();
                continue;
            }

            released++;
            var title = order.Listing?.Title ?? "Your item";
            await _messaging.CreateSystemMessageAsync(order.SellerId, SystemMessageType.EscrowReleased,
                $"Payment of €{order.SellerPayout:N0} has been released to your Stripe account.",
                $"/Orders/Details/{order.Id}");
            await _emailNotifications.NotifyOrderCompletedSellerAsync(order.SellerId, title, order.Id);
            await _emailNotifications.NotifyOrderCompletedBuyerAsync(order.BuyerId, title, order.Id);
        }

        return released;
    }

    public async Task RefundBuyerAsync(Guid orderId)
    {
        var order = await _context.Orders
            .Include(o => o.Payment)
            .Include(o => o.Listing)
            .Include(o => o.Items).ThenInclude(i => i.Listing)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new Exception("Order not found");

        // Refund from platform — money was never transferred to seller
        if (order.Payment?.StripePaymentIntentId != null)
        {
            var refunded = await _payment.RefundPaymentAsync(order.Payment.StripePaymentIntentId);
            if (!refunded)
                throw new Exception($"Stripe refund failed for order {orderId}.");
        }

        order.Status = OrderStatus.Refunded;

        // Re-list what was sold: one listing, or every listing of a bundle order
        if (order.Listing != null)
            order.Listing.Status = ListingStatus.Active;
        foreach (var item in order.Items)
            item.Listing.Status = ListingStatus.Active;

        if (order.Payment != null)
            order.Payment.Status = PaymentStatus.Refunded;

        await _context.SaveChangesAsync();
    }

    public async Task RefundBuyerPartialAsync(Guid orderId, decimal partialRefundAmount)
    {
        var order = await _context.Orders
            .Include(o => o.Payment)
            .Include(o => o.Seller)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new Exception("Order not found.");

        if (partialRefundAmount <= 0 || partialRefundAmount >= order.FinalPrice)
            throw new Exception($"Partial refund amount must be between €0.01 and €{order.FinalPrice - 0.01m:N2}.");

        if (order.Payment?.StripePaymentIntentId != null)
        {
            // Partial refund to buyer
            var refunded = await _payment.RefundPaymentAsync(order.Payment.StripePaymentIntentId, partialRefundAmount);
            if (!refunded)
                throw new Exception($"Stripe partial refund failed for order {orderId}.");

            // Transfer remaining seller payout (original payout minus the partial refund amount)
            // Platform keeps its fee; seller bears the cost of the refund.
            var adjustedPayout = order.SellerPayout - partialRefundAmount;
            if (adjustedPayout > 0 && order.Seller?.StripeAccountId != null)
                await _payment.CreateTransferAsync(adjustedPayout, order.Seller.StripeAccountId, orderId.ToString(), order.Payment.StripePaymentIntentId);
            order.SellerPayout = adjustedPayout;

            order.Payment.ReleasedAt = DateTime.UtcNow;
        }

        // Partial refund: transaction is complete, item is not re-listed
        order.Status = OrderStatus.Completed;
        order.CompletedAt = DateTime.UtcNow;

        if (order.Payment != null)
        {
            order.Payment.Status = PaymentStatus.PartialRefund;
            order.Payment.ReleasedAt = DateTime.UtcNow;
        }

        var sellerProfile = await _context.UserProfiles.FindAsync(order.SellerId);
        if (sellerProfile != null)
            sellerProfile.TotalSales++;

        await _context.SaveChangesAsync();
    }

    public decimal CalculatePlatformFee(decimal price) => PlatformFee.Calculate(price);
}
