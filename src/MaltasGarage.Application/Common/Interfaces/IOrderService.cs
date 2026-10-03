using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Application.Common.Interfaces;

public interface IOrderService
{
    /// <summary>
    /// What a buyer pays for a listing: the accepted offer when <paramref name="offerId"/> is
    /// that buyer's accepted offer on that listing, otherwise the Buy Now price. Never a price
    /// taken from the request. Throws when the offer cannot be used.
    /// </summary>
    Task<decimal> GetCheckoutPriceAsync(Guid listingId, Guid buyerId, Guid? offerId);
    /// <summary>Creates the order for one listing. With a payment the order is Paid and saved with it in one write.</summary>
    Task<Order> CreateOrderAsync(Guid listingId, Guid buyerId, decimal price, DeliveryMethod? deliveryMethod, OrderPayment? payment = null);
    Task<Order> CreateBundleOrderAsync(Guid bundleOfferId, Guid buyerId, decimal totalPrice, DeliveryMethod? deliveryMethod, OrderPayment? payment = null);
    /// <summary>
    /// Marks a paid MaltaPost order as shipped by its seller. Throws for any other seller, status
    /// or delivery method, so a disputed order can never start the auto-release clock.
    /// </summary>
    Task<Order> MarkShippedAsync(Guid orderId, Guid sellerId, string trackingNumber);
    Task ReleaseEscrowAsync(Guid orderId);

    /// <summary>
    /// Releases escrow the buyer never confirmed: MaltaPost orders 5 days after shipping,
    /// hand-to-hand orders 7 days after payment. Idempotent; returns how many were released.
    /// </summary>
    Task<int> AutoReleaseDueEscrowAsync(DateTime now);
    Task RefundBuyerAsync(Guid orderId);
    Task RefundBuyerPartialAsync(Guid orderId, decimal partialRefundAmount);
    decimal CalculatePlatformFee(decimal price);
}
