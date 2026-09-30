using MaltasGarage.Application.Common.Interfaces;

namespace MaltasGarage.Tests.Stubs;

public class NoOpPaymentService : IPaymentService
{
    public Task<string> CreateConnectedAccountAsync(string email, string country = "MT") => Task.FromResult("acct_test");
    public Task<string> CreateAccountLinkAsync(string accountId, string returnUrl, string refreshUrl) => Task.FromResult("https://stripe.com/test");
    public Task<bool> IsAccountReadyAsync(string accountId) => Task.FromResult(true);
    public Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string connectedAccountId)
        => Task.FromResult(new PaymentIntentResult { Success = true, PaymentIntentId = "pi_test", ClientSecret = "pi_test_secret" });
    public Task<bool> CreateTransferAsync(decimal sellerPayout, string connectedAccountId, string transferGroup, string? paymentIntentId = null) => Task.FromResult(true);
    public Task<bool> RefundPaymentAsync(string paymentIntentId, decimal? amount = null) => Task.FromResult(true);
    public Task<(string PaymentIntentId, string ClientSecret)> CreateBidPaymentAsync(decimal amount, string connectedAccountId)
        => Task.FromResult(("pi_test", "pi_test_secret"));
    public Task<string> CreateLoginLinkAsync(string accountId) => Task.FromResult("https://stripe.com/login");
}
