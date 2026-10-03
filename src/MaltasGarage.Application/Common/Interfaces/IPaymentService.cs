using MaltasGarage.Application.Common.Models;

namespace MaltasGarage.Application.Common.Interfaces;

public interface IPaymentService
{
    Task<string> CreateConnectedAccountAsync(string email, string country = "MT");
    Task<string> CreateAccountLinkAsync(string accountId, string returnUrl, string refreshUrl);
    Task<bool> IsAccountReadyAsync(string accountId);
    /// <summary>
    /// Creates a PaymentIntent for an amount the server computed. <paramref name="metadata"/>
    /// (see <see cref="PaymentMetadata"/>) ties it to what is being paid for and by whom.
    /// </summary>
    Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, IReadOnlyDictionary<string, string>? metadata = null);
    /// <summary>
    /// Pays the seller their share. <paramref name="transferGroup"/> must be the order id: it is
    /// also the idempotency key, so repeating the call for the same order (within Stripe's ~24 h key
    /// window) never pays twice. Returns the Stripe transfer id, or null when the transfer failed.
    /// </summary>
    Task<string?> CreateTransferAsync(decimal sellerPayout, string connectedAccountId, string transferGroup, string? paymentIntentId = null);
    Task<bool> RefundPaymentAsync(string paymentIntentId, decimal? amount = null);
    Task<(string PaymentIntentId, string ClientSecret)> CreateBidPaymentAsync(decimal amount, IReadOnlyDictionary<string, string>? metadata = null);
    /// <summary>
    /// The payment as Stripe reports it, or null when it has not succeeded. The amount and
    /// metadata come from Stripe, so they cannot be changed by the buyer.
    /// </summary>
    Task<ConfirmedPayment?> GetSucceededPaymentAsync(string paymentIntentId);
    Task<string> CreateLoginLinkAsync(string accountId);
}

public class PaymentIntentResult
{
    public bool Success { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Error { get; set; }
}
