using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Application.Common.Interfaces;

/// <summary>The outcome of turning a succeeded payment into an order or a bid.</summary>
public record PurchaseResult(bool Succeeded, Guid? OrderId = null, string? Error = null)
{
    public static PurchaseResult Done(Guid? orderId = null) => new(true, orderId);
    public static PurchaseResult Failed(string error) => new(false, Error: error);
}

/// <summary>
/// Finishes a purchase once Stripe has taken the money: a Buy Now order, a bundle order or a bid.
/// Both the buyer's return from Stripe and the payment_intent.succeeded webhook call it, so a
/// buyer who closes the tab after paying still gets the order. Safe to call more than once for
/// the same payment: the second call finds the first one's result. When the purchase can no
/// longer happen (the item sold in the meantime) the payment is refunded.
/// </summary>
public interface IPurchaseCompletionService
{
    /// <param name="payment">A payment Stripe reports as succeeded, with the metadata this server set.</param>
    /// <param name="deliveryMethod">The buyer's choice from checkout; null (the webhook) lets them choose on the order page.</param>
    Task<PurchaseResult> CompleteAsync(ConfirmedPayment payment, DeliveryMethod? deliveryMethod = null);
}
