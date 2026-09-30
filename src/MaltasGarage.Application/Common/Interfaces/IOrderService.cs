using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Application.Common.Interfaces;

public interface IOrderService
{
    Task<Order> CreateOrderAsync(Guid listingId, Guid buyerId, decimal price, DeliveryMethod deliveryMethod);
    Task<Order> CreateBundleOrderAsync(Guid bundleOfferId, Guid buyerId, decimal totalPrice, DeliveryMethod deliveryMethod);
    Task<Order?> GetOrderAsync(Guid orderId);
    Task<List<Order>> GetBuyerOrdersAsync(Guid buyerId);
    Task<List<Order>> GetSellerOrdersAsync(Guid sellerId);
    Task UpdateStatusAsync(Guid orderId, OrderStatus status);
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
