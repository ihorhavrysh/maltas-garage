using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
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
    private readonly TimeProvider _time;

    public OrderService(ApplicationDbContext context, IMessagingService messaging,
        IEmailNotificationService emailNotifications, IPaymentService payment, TimeProvider time)
    {
        _context = context;
        _messaging = messaging;
        _emailNotifications = emailNotifications;
        _payment = payment;
        _time = time;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

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

        // An accepted offer must be paid within its window (PriceOfferService.AcceptedOfferPayWindow)
        if (offer.ExpiresAt <= Now)
            throw new InvalidOperationException("This offer has expired. Make a new offer or buy at the listed price.");

        return offer.Amount;
    }

    public async Task<Order> CreateOrderAsync(Guid listingId, Guid buyerId, decimal price, DeliveryMethod? deliveryMethod,
        OrderPayment? payment = null)
    {
        var listing = await _context.Listings
            .FirstOrDefaultAsync(l => l.Id == listingId)
            ?? throw new InvalidOperationException("Listing not found.");

        listing.EnsureNotShowcase();

        if (listing.Status is not (ListingStatus.Active or ListingStatus.AuctionPhase))
            throw new InvalidOperationException(listing.Status == ListingStatus.Sold
                ? "Listing already sold."
                : "This listing is no longer available.");

        var order = NewOrder(buyerId, listing.SellerId, price, deliveryMethod, payment);
        order.ListingId = listingId;

        listing.Status = ListingStatus.Sold;
        listing.CurrentPrice = price;

        _context.Orders.Add(order);
        await CompleteOfferAsync(payment);

        // Order, payment, listing and offer in one SaveChanges: nothing half-done if it fails,
        // and the listing's concurrency stamp stops a second buyer at the same moment
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

    public async Task<Order> CreateBundleOrderAsync(Guid bundleOfferId, Guid buyerId, decimal totalPrice, DeliveryMethod? deliveryMethod,
        OrderPayment? payment = null)
    {
        var bundleOffer = await _context.BundleOffers
            .Include(o => o.Items).ThenInclude(i => i.Listing)
            .FirstOrDefaultAsync(o => o.Id == bundleOfferId)
            ?? throw new InvalidOperationException("Bundle offer not found.");

        if (bundleOffer.Status != Domain.Enums.BundleOfferStatus.Accepted)
            throw new InvalidOperationException("Bundle offer is not accepted.");

        var listings = bundleOffer.Items.Select(i => i.Listing).ToList();
        listings.ForEach(l => l.EnsureNotShowcase());

        if (listings.Any(l => l.Status != ListingStatus.Active))
            throw new InvalidOperationException("One or more listings are no longer available.");

        var order = NewOrder(buyerId, bundleOffer.SellerId, totalPrice, deliveryMethod, payment);
        order.IsBundleOrder = true;

        foreach (var item in bundleOffer.Items)
        {
            order.Items.Add(new OrderItem
            {
                Id        = Guid.NewGuid(),
                ListingId = item.ListingId,
                Price     = item.ListedPrice,
                CreatedAt = Now
            });

            item.Listing.Status       = ListingStatus.Sold;
            item.Listing.CurrentPrice = item.ListedPrice;
        }

        bundleOffer.Status    = Domain.Enums.BundleOfferStatus.Completed;
        bundleOffer.UpdatedAt = Now;

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();   // one transaction for the order, its items and the listings

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

    // A paid order starts as Paid with its captured payment; without a payment it is Pending.
    // Without a delivery method the buyer chooses one later on the order page
    private Order NewOrder(Guid buyerId, Guid sellerId, decimal price, DeliveryMethod? deliveryMethod, OrderPayment? payment)
    {
        var platformFee = CalculatePlatformFee(price);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            FinalPrice = price,
            PlatformFee = platformFee,
            SellerPayout = price - platformFee,
            DeliveryMethod = deliveryMethod ?? DeliveryMethod.HandToHand,
            DeliveryMethodPending = deliveryMethod == null,
            Status = payment == null ? OrderStatus.Pending : OrderStatus.Paid,
            PaidAt = payment == null ? null : Now,
            CreatedAt = Now
        };

        if (payment != null)
        {
            order.Payment = new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                StripePaymentIntentId = payment.PaymentIntentId,
                Amount = price,
                Status = PaymentStatus.Captured,
                CreatedAt = Now,
                CapturedAt = Now
            };
        }

        return order;
    }

    private async Task CompleteOfferAsync(OrderPayment? payment)
    {
        if (payment?.OfferId == null) return;

        var offer = await _context.PriceOffers.FindAsync(payment.OfferId.Value);
        if (offer != null)
        {
            offer.Status = PriceOfferStatus.Completed;
            offer.UpdatedAt = Now;
        }
    }

    public async Task<Order> MarkShippedAsync(Guid orderId, Guid sellerId, string trackingNumber)
    {
        var order = await _context.Orders
            .Include(o => o.Listing)
            .Include(o => o.Items)
            .Include(o => o.Shipment)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException("Order not found.");

        if (order.SellerId != sellerId)
            throw new InvalidOperationException("Only the seller can ship this order.");

        // Checked here, not only on the page: a disputed or hand-to-hand order must never become
        // Shipped, because Shipped starts the MaltaPost auto-release clock
        if (order.Status != OrderStatus.Paid)
            throw new InvalidOperationException("Order is not ready for shipping.");
        if (order.DeliveryMethod != DeliveryMethod.MaltaPost)
            throw new InvalidOperationException("This order uses hand-to-hand delivery.");
        if (order.Shipment != null)
            throw new InvalidOperationException("This order has already been shipped.");

        _context.Shipments.Add(new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Method = DeliveryMethod.MaltaPost,
            Carrier = "MaltaPost",
            TrackingNumber = trackingNumber.Trim(),
            Status = ShipmentStatus.Shipped,
            ShippedAt = Now,
            DeliveryDeadline = Now.AddDays(7),
            CreatedAt = Now
        });

        order.Status = OrderStatus.Shipped;
        order.UpdatedAt = Now;

        await _context.SaveChangesAsync();
        return order;
    }

    public async Task ReleaseEscrowAsync(Guid orderId)
    {
        var order = await _context.Orders
            .Include(o => o.Payment)
            .Include(o => o.Seller)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException("Order not found");

        if (order.Status == OrderStatus.Completed || order.Status == OrderStatus.Refunded)
            throw new InvalidOperationException("Order already resolved");

        // Escrow can only be released once the buyer has paid: Paid (hand-to-hand),
        // Shipped/Delivered (MaltaPost) or Disputed (resolved in favour of the seller).
        if (order.Status is not (OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Disputed))
            throw new InvalidOperationException($"Cannot release escrow for order {orderId}: order status is {order.Status}");

        if (order.Payment == null)
            throw new InvalidOperationException($"Cannot release escrow for order {orderId}: Payment is null");

        // No PaymentIntent means no money went through Stripe (seeded demo orders), so there is
        // nothing to transfer: release in the database only, as RefundBuyerAsync and
        // AutoReleaseDueEscrowAsync already do
        if (order.Payment.StripePaymentIntentId != null)
        {
            if (order.Seller?.StripeAccountId == null)
                throw new InvalidOperationException($"Cannot release escrow for order {orderId}: Seller StripeAccountId is null");
            if (order.Payment.Status != PaymentStatus.Captured)
                throw new InvalidOperationException($"Cannot release escrow for order {orderId}: Payment.Status is {order.Payment.Status} (expected Captured)");

            // Transfer seller's payout — this is when money actually moves from platform to seller
            var transferred = await _payment.CreateTransferAsync(
                order.SellerPayout,
                order.Seller.StripeAccountId,
                orderId.ToString(),
                order.Payment.StripePaymentIntentId);

            if (!transferred)
                throw new InvalidOperationException($"Stripe transfer failed for order {orderId}.");
        }

        order.Payment.ReleasedAt = Now;

        order.Status = OrderStatus.Completed;
        order.CompletedAt = Now;

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

            // Transfer first: it is idempotent per order, so a retry or a race is safe. A failed
            // transfer skips this order only; the next sweep tries it again
            if (order.Payment!.StripePaymentIntentId != null && order.Seller.StripeAccountId != null)
            {
                bool transferred;
                try
                {
                    transferred = await _payment.CreateTransferAsync(order.SellerPayout, order.Seller.StripeAccountId,
                        order.Id.ToString(), order.Payment.StripePaymentIntentId);
                }
                catch (Exception)
                {
                    transferred = false;
                }

                if (!transferred)
                {
                    _context.ChangeTracker.Clear();
                    continue;
                }
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
            ?? throw new InvalidOperationException("Order not found");

        // Only money that is still held in escrow can be refunded. Without this a second
        // resolution, a double click or a late request would refund an order twice
        EnsureRefundable(order);

        // Refund from platform — money was never transferred to seller
        if (order.Payment?.StripePaymentIntentId != null)
        {
            var refunded = await _payment.RefundPaymentAsync(order.Payment.StripePaymentIntentId);
            if (!refunded)
                throw new InvalidOperationException($"Stripe refund failed for order {orderId}.");
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
            ?? throw new InvalidOperationException("Order not found.");

        EnsureRefundable(order);

        // The seller bears the refund and the platform keeps its fee, so the refund can be at most
        // the seller's payout; anything more would make the payout negative
        if (partialRefundAmount <= 0 || partialRefundAmount > order.SellerPayout)
            throw new InvalidOperationException($"Partial refund amount must be between €0.01 and €{order.SellerPayout:N2}.");

        var adjustedPayout = order.SellerPayout - partialRefundAmount;

        if (order.Payment?.StripePaymentIntentId != null)
        {
            // Partial refund to buyer
            var refunded = await _payment.RefundPaymentAsync(order.Payment.StripePaymentIntentId, partialRefundAmount);
            if (!refunded)
                throw new InvalidOperationException($"Stripe partial refund failed for order {orderId}.");

            // Transfer the rest of the seller payout. If it fails the order stays open, so the
            // resolution can be repeated: the refund and the transfer are both idempotent
            if (adjustedPayout > 0 && order.Seller?.StripeAccountId != null &&
                !await _payment.CreateTransferAsync(adjustedPayout, order.Seller.StripeAccountId, orderId.ToString(), order.Payment.StripePaymentIntentId))
                throw new InvalidOperationException($"The buyer was refunded, but the transfer to the seller failed for order {orderId}. Try resolving again.");
        }

        order.SellerPayout = adjustedPayout;

        // Partial refund: transaction is complete, item is not re-listed
        order.Status = OrderStatus.Completed;
        order.CompletedAt = Now;

        if (order.Payment != null)
        {
            order.Payment.Status = PaymentStatus.PartialRefund;
            order.Payment.ReleasedAt = Now;
        }

        var sellerProfile = await _context.UserProfiles.FindAsync(order.SellerId);
        if (sellerProfile != null)
            sellerProfile.TotalSales++;

        await _context.SaveChangesAsync();
    }

    private static void EnsureRefundable(Order order)
    {
        if (order.Status is not (OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Disputed))
            throw new InvalidOperationException($"Order {order.Id} cannot be refunded: its status is {order.Status}.");
        if (order.Payment != null && order.Payment.Status != PaymentStatus.Captured)
            throw new InvalidOperationException($"Order {order.Id} cannot be refunded: the payment is {order.Payment.Status}.");
    }

    public decimal CalculatePlatformFee(decimal price) => PlatformFee.Calculate(price);
}
