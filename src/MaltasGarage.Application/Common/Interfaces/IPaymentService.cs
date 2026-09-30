namespace MaltasGarage.Application.Common.Interfaces;

public interface IPaymentService
{
    Task<string> CreateConnectedAccountAsync(string email, string country = "MT");
    Task<string> CreateAccountLinkAsync(string accountId, string returnUrl, string refreshUrl);
    Task<bool> IsAccountReadyAsync(string accountId);
    Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string connectedAccountId);
    /// <summary>
    /// Pays the seller their share. <paramref name="transferGroup"/> must be the order id: it is
    /// also the idempotency key, so repeating the call for the same order never pays twice.
    /// </summary>
    Task<bool> CreateTransferAsync(decimal sellerPayout, string connectedAccountId, string transferGroup, string? paymentIntentId = null);
    Task<bool> RefundPaymentAsync(string paymentIntentId, decimal? amount = null);
    Task<(string PaymentIntentId, string ClientSecret)> CreateBidPaymentAsync(decimal amount, string connectedAccountId);
    Task<string> CreateLoginLinkAsync(string accountId);
}

public class PaymentIntentResult
{
    public bool Success { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Error { get; set; }
}
