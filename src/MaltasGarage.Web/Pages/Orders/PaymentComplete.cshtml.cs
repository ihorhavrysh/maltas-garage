using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Orders;

[Authorize]
public class PaymentCompleteModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IOrderService _orderService;
    private readonly IPaymentService _paymentService;
    private readonly IMessagingService _messaging;

    public PaymentCompleteModel(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IOrderService orderService,
        IPaymentService paymentService,
        IMessagingService messaging)
    {
        _context = context;
        _currentUser = currentUser;
        _orderService = orderService;
        _paymentService = paymentService;
        _messaging = messaging;
    }

    public async Task<IActionResult> OnGetAsync(
        Guid listingId,
        string deliveryMethod,
        string? payment_intent,
        string? redirect_status)
    {
        if (redirect_status != "succeeded" || string.IsNullOrEmpty(payment_intent))
        {
            TempData["Error"] = "Payment was not completed. Please try again.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        // Prevent double-processing if user refreshes
        var existing = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == payment_intent);
        if (existing != null)
            return RedirectToPage("/Orders/Confirmation", new { orderId = existing.OrderId });

        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (userProfile == null)
            return RedirectToPage("/Listing", new { id = listingId });

        // The amount and the offer come from Stripe: everything in this URL is the buyer's to edit.
        // A payment made for another listing or by another account is not used here.
        var payment = await _paymentService.GetSucceededPaymentAsync(payment_intent);
        if (payment == null || !payment.IsFor(PaymentMetadata.BuyNow, listingId, userProfile.Id))
        {
            TempData["Error"] = "Payment verification failed.";
            return RedirectToPage("/Listing", new { id = listingId });
        }

        var price = payment.Amount;
        Guid? offerId = Guid.TryParse(payment.Get(PaymentMetadata.OfferId), out var parsedOfferId) ? parsedOfferId : null;

        // If the listing was in AuctionPhase, refund the current top bidder (Buy Now beats the auction)
        var listing = await _context.Listings
            .Include(l => l.Bids)
            .FirstOrDefaultAsync(l => l.Id == listingId);

        if (listing?.Status == Domain.Enums.ListingStatus.AuctionPhase && listing.Bids.Any())
        {
            var topBid = listing.Bids.OrderByDescending(b => b.Amount).First();
            if (topBid.StripePaymentIntentId != null)
                await _paymentService.RefundPaymentAsync(topBid.StripePaymentIntentId);

            await _messaging.CreateSystemMessageAsync(
                topBid.BidderId,
                Domain.Enums.SystemMessageType.BidOutbid,
                $"Someone purchased \"{listing.Title}\" using Buy Now. Your bid has been refunded.",
                $"/Listing/{listingId}");
        }

        // Create order and mark listing as Sold
        if (!Enum.TryParse<DeliveryMethod>(deliveryMethod, out var method))
            method = DeliveryMethod.HandToHand;

        Domain.Entities.Order order;
        try
        {
            order = await _orderService.CreateOrderAsync(listingId, userProfile.Id, price, method);
        }
        catch (Exception ex)
        {
            // Payment was already captured — refund it since we can't create the order
            await _paymentService.RefundPaymentAsync(payment_intent);
            TempData["Error"] = ex.Message;
            return RedirectToPage("/Listing", new { id = listingId });
        }

        order.Status = OrderStatus.Paid;
        order.PaidAt = DateTime.UtcNow;

        if (offerId.HasValue)
        {
            var priceOffer = await _context.PriceOffers.FindAsync(offerId.Value);
            if (priceOffer != null)
            {
                priceOffer.Status    = Domain.Enums.PriceOfferStatus.Completed;
                priceOffer.UpdatedAt = DateTime.UtcNow;
            }
        }

        _context.Payments.Add(new Domain.Entities.Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            StripePaymentIntentId = payment_intent,
            Amount = price,
            Status = PaymentStatus.Captured,
            CreatedAt = DateTime.UtcNow,
            CapturedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return RedirectToPage("/Orders/Confirmation", new { orderId = order.Id });
    }
}
