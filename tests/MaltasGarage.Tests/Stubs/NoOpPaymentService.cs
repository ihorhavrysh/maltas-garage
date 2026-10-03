using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;

namespace MaltasGarage.Tests.Stubs;

/// <summary>Payment stub where every call succeeds. Methods are virtual so tests can override one.</summary>
public class NoOpPaymentService : IPaymentService
{
    public virtual Task<string> CreateConnectedAccountAsync(string email, string country = "MT") => Task.FromResult("acct_test");
    public virtual Task<string> CreateAccountLinkAsync(string accountId, string returnUrl, string refreshUrl) => Task.FromResult("https://stripe.com/test");
    public virtual Task<bool> IsAccountReadyAsync(string accountId) => Task.FromResult(true);
    public virtual Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string connectedAccountId, IReadOnlyDictionary<string, string>? metadata = null)
        => Task.FromResult(new PaymentIntentResult { Success = true, PaymentIntentId = "pi_test", ClientSecret = "pi_test_secret" });
    public virtual Task<bool> CreateTransferAsync(decimal sellerPayout, string connectedAccountId, string transferGroup, string? paymentIntentId = null) => Task.FromResult(true);
    public virtual Task<bool> RefundPaymentAsync(string paymentIntentId, decimal? amount = null) => Task.FromResult(true);
    public virtual Task<(string PaymentIntentId, string ClientSecret)> CreateBidPaymentAsync(decimal amount, string connectedAccountId, IReadOnlyDictionary<string, string>? metadata = null)
        => Task.FromResult(("pi_test", "pi_test_secret"));
    public virtual Task<ConfirmedPayment?> GetSucceededPaymentAsync(string paymentIntentId) => Task.FromResult<ConfirmedPayment?>(null);
    public virtual Task<string> CreateLoginLinkAsync(string accountId) => Task.FromResult("https://stripe.com/login");
}
