using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MaltasGarage.Infrastructure.Services;

public class PurchaseCompletionService : IPurchaseCompletionService
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderService _orders;
    private readonly IBiddingService _bidding;
    private readonly IPriceOfferService _offers;
    private readonly IPaymentService _payments;
    private readonly IMessagingService _messaging;
    private readonly ILogger<PurchaseCompletionService> _logger;

    public PurchaseCompletionService(
        ApplicationDbContext context,
        IOrderService orders,
        IBiddingService bidding,
        IPriceOfferService offers,
        IPaymentService payments,
        IMessagingService messaging,
        ILogger<PurchaseCompletionService> logger)
    {
        _context = context;
        _orders = orders;
        _bidding = bidding;
        _offers = offers;
        _payments = payments;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<PurchaseResult> CompleteAsync(ConfirmedPayment payment, DeliveryMethod? deliveryMethod = null)
    {
        if (!Guid.TryParse(payment.Get(PaymentMetadata.SubjectId), out var subjectId) ||
            !Guid.TryParse(payment.Get(PaymentMetadata.PayerId), out var buyerId))
            return PurchaseResult.Failed("This payment was not made through the marketplace.");

        return payment.Get(PaymentMetadata.Purpose) switch
        {
            PaymentMetadata.BuyNow => await CompleteOrderAsync(payment, deliveryMethod,
                () => BuyNowAsync(payment, subjectId, buyerId, deliveryMethod)),
            PaymentMetadata.Bundle => await CompleteOrderAsync(payment, deliveryMethod,
                () => BundleAsync(payment, subjectId, buyerId, deliveryMethod)),
            PaymentMetadata.Bid => await CompleteBidAsync(payment, subjectId, buyerId),
            _ => PurchaseResult.Failed("This payment was not made through the marketplace.")
        };
    }

    private async Task<PurchaseResult> CompleteOrderAsync(ConfirmedPayment payment, DeliveryMethod? deliveryMethod,
        Func<Task<Guid>> createOrder)
    {
        // The order and its payment row are saved together, so a payment row means the order exists
        var existing = await FindOrderForPaymentAsync(payment.PaymentIntentId, deliveryMethod);
        if (existing != null)
            return PurchaseResult.Done(existing);

        try
        {
            return PurchaseResult.Done(await createOrder());
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            // The other caller (page or webhook) may have just saved this payment's order; the
            // unique index on the PaymentIntent id makes this one fail. That is not a refund case
            _context.ChangeTracker.Clear();
            existing = await FindOrderForPaymentAsync(payment.PaymentIntentId, deliveryMethod);
            if (existing != null)
                return PurchaseResult.Done(existing);

            _logger.LogWarning("Payment {PaymentIntentId} could not become an order and is refunded: {Message}",
                payment.PaymentIntentId, ex.Message);
            await _payments.RefundPaymentAsync(payment.PaymentIntentId);
            return PurchaseResult.Failed(ex is DbUpdateException
                ? "Someone else bought this item at the same moment. Your payment has been refunded."
                : ex.Message + " Your payment has been refunded.");
        }
    }

    private async Task<Guid> BuyNowAsync(ConfirmedPayment payment, Guid listingId, Guid buyerId, DeliveryMethod? deliveryMethod)
    {
        Guid? offerId = Guid.TryParse(payment.Get(PaymentMetadata.OfferId), out var parsed) ? parsed : null;

        // Read before the order: the listing is Sold afterwards
        var listing = await _context.Listings.Include(l => l.Bids).FirstOrDefaultAsync(l => l.Id == listingId);
        var outbidBid = listing?.Status == ListingStatus.AuctionPhase
            ? listing.Bids.OrderByDescending(b => b.Amount).FirstOrDefault()
            : null;

        var order = await _orders.CreateOrderAsync(listingId, buyerId, payment.Amount, deliveryMethod,
            new OrderPayment(payment.PaymentIntentId, offerId));

        // Buy Now beat the auction: the top bidder's held money goes back, and only once the order
        // exists (a Buy Now that fails must not cost the auction its leader)
        if (outbidBid != null)
        {
            if (outbidBid.StripePaymentIntentId != null)
                await _payments.RefundPaymentAsync(outbidBid.StripePaymentIntentId);

            await _messaging.CreateSystemMessageAsync(
                outbidBid.BidderId,
                SystemMessageType.BidOutbid,
                $"Someone purchased \"{listing!.Title}\" using Buy Now. Your bid has been refunded.",
                $"/Listing/{listingId}");
        }

        await _offers.CancelOpenOffersForListingAsync(listingId);
        return order.Id;
    }

    private async Task<Guid> BundleAsync(ConfirmedPayment payment, Guid bundleOfferId, Guid buyerId, DeliveryMethod? deliveryMethod)
    {
        var order = await _orders.CreateBundleOrderAsync(bundleOfferId, buyerId, payment.Amount, deliveryMethod,
            new OrderPayment(payment.PaymentIntentId));

        foreach (var listingId in order.Items.Select(i => i.ListingId).ToList())
            await _offers.CancelOpenOffersForListingAsync(listingId);

        return order.Id;
    }

    private async Task<PurchaseResult> CompleteBidAsync(ConfirmedPayment payment, Guid listingId, Guid bidderId)
    {
        if (await BidRecordedAsync(payment.PaymentIntentId))
            return PurchaseResult.Done();

        // The previous leader (another bidder) gets their held money back once this bid stands
        var previousTopBid = await _context.Bids
            .Where(b => b.ListingId == listingId && b.BidderId != bidderId)
            .OrderByDescending(b => b.Amount)
            .FirstOrDefaultAsync();

        BidResult result;
        try
        {
            result = await _bidding.PlaceBidAsync(listingId, bidderId, payment.Amount, payment.PaymentIntentId);
        }
        catch (DbUpdateException)
        {
            result = new BidResult { Success = false, Error = "Another bid was placed at the same moment." };
        }

        if (!result.Success)
        {
            // The page and the webhook can record the same bid side by side; the one that loses
            // must not refund the bid the other one just placed
            _context.ChangeTracker.Clear();
            if (await BidRecordedAsync(payment.PaymentIntentId))
                return PurchaseResult.Done();

            await _payments.RefundPaymentAsync(payment.PaymentIntentId);
            return PurchaseResult.Failed((result.Error ?? "Your bid could not be placed.") + " Your payment has been refunded.");
        }

        if (previousTopBid?.StripePaymentIntentId != null)
            await _payments.RefundPaymentAsync(previousTopBid.StripePaymentIntentId);

        return PurchaseResult.Done();
    }

    private Task<bool> BidRecordedAsync(string paymentIntentId) =>
        _context.Bids.AnyAsync(b => b.StripePaymentIntentId == paymentIntentId);

    // When the webhook created the order first it had no delivery method; the buyer's choice
    // from checkout, arriving a moment later, is applied then
    private async Task<Guid?> FindOrderForPaymentAsync(string paymentIntentId, DeliveryMethod? deliveryMethod)
    {
        var existing = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId);
        if (existing == null)
            return null;

        if (deliveryMethod != null && existing.Order.DeliveryMethodPending)
        {
            existing.Order.DeliveryMethod = deliveryMethod.Value;
            existing.Order.DeliveryMethodPending = false;
            await _context.SaveChangesAsync();
        }

        return existing.OrderId;
    }
}
