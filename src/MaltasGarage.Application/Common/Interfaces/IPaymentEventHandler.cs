namespace MaltasGarage.Application.Common.Interfaces;

/// <summary>
/// Applies verified Stripe webhook events to the database. The webhook endpoint checks the
/// signature; this handler decides whether the event still means anything. Stripe can deliver
/// an event more than once and in any order, so every method is safe to call repeatedly.
/// </summary>
public interface IPaymentEventHandler
{
    /// <summary>
    /// A PaymentIntent succeeded. Confirms the matching payment only while it is still Pending;
    /// a payment that is already captured, released or refunded is left alone. Returns whether
    /// anything changed.
    /// </summary>
    Task<bool> PaymentSucceededAsync(string paymentIntentId);

    /// <summary>A connected account can now take charges and receive payouts.</summary>
    Task SellerAccountReadyAsync(string stripeAccountId);
}
