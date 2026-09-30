using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace MaltasGarage.Infrastructure.Services;

public class StripePaymentService : IPaymentService
{
    private readonly StripeSettings _settings;
    private readonly ILogger<StripePaymentService> _logger;
    private readonly string _baseUrl;

    public StripePaymentService(IOptions<StripeSettings> settings, IOptions<AppSettings> appSettings, ILogger<StripePaymentService> logger)
    {
        _settings = settings.Value;
        _baseUrl = appSettings.Value.BaseUrl.TrimEnd('/');
        _logger = logger;
        StripeConfiguration.ApiKey = _settings.SecretKey;
    }

    public async Task<string> CreateConnectedAccountAsync(string email, string country = "MT")
    {
        var options = new AccountCreateOptions
        {
            Type = "express",
            Email = email,
            Country = country,
            BusinessType = "individual",
            BusinessProfile = new AccountBusinessProfileOptions
            {
                Mcc = "5931",
                ProductDescription = "Selling second-hand personal items through Malta's Garage marketplace",
                Url = _baseUrl
            },
            Capabilities = new AccountCapabilitiesOptions
            {
                CardPayments = new AccountCapabilitiesCardPaymentsOptions { Requested = true },
                Transfers = new AccountCapabilitiesTransfersOptions { Requested = true }
            }
        };

        var service = new AccountService();
        var account = await service.CreateAsync(options);
        return account.Id;
    }

    public async Task<string> CreateAccountLinkAsync(string accountId, string returnUrl, string refreshUrl)
    {
        var options = new AccountLinkCreateOptions
        {
            Account = accountId,
            RefreshUrl = refreshUrl,
            ReturnUrl = returnUrl,
            Type = "account_onboarding"
        };

        var service = new AccountLinkService();
        var link = await service.CreateAsync(options);
        return link.Url;
    }

    public async Task<bool> IsAccountReadyAsync(string accountId)
    {
        var service = new AccountService();
        var account = await service.GetAsync(accountId);
        return account.ChargesEnabled && account.PayoutsEnabled;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string connectedAccountId)
    {
        try
        {
            // Platform captures the full amount. Transfer to seller happens manually
            // via CreateTransferAsync when the buyer confirms receipt (escrow release).
            var options = new PaymentIntentCreateOptions
            {
                Amount = (long)(amount * 100),
                Currency = "eur",
                PaymentMethodTypes = new List<string> { "card" },
            };

            var service = new PaymentIntentService();
            var intent = await service.CreateAsync(options);

            return new PaymentIntentResult
            {
                Success = true,
                PaymentIntentId = intent.Id,
                ClientSecret = intent.ClientSecret
            };
        }
        catch (StripeException ex)
        {
            return new PaymentIntentResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<bool> CreateTransferAsync(decimal sellerPayout, string connectedAccountId, string transferGroup, string? paymentIntentId = null)
    {
        try
        {
            var options = new TransferCreateOptions
            {
                Amount = (long)(sellerPayout * 100),
                Currency = "eur",
                Destination = connectedAccountId,
                TransferGroup = transferGroup
            };

            // Link transfer to original charge so it works against pending balance
            if (paymentIntentId != null)
            {
                var piService = new PaymentIntentService();
                var pi = await piService.GetAsync(paymentIntentId);
                if (pi.LatestChargeId != null)
                    options.SourceTransaction = pi.LatestChargeId;
            }

            // One transfer per order, ever: Stripe returns the original transfer for a repeated
            // key, so a manual release racing the automatic one cannot pay the seller twice.
            var requestOptions = new RequestOptions { IdempotencyKey = $"escrow-release-{transferGroup}" };

            var service = new TransferService();
            await service.CreateAsync(options, requestOptions);
            return true;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe Transfer failed — payout={Payout} destination={Dest} group={Group} | {Message}",
                sellerPayout, connectedAccountId, transferGroup, ex.Message);
            throw;
        }
    }

    public async Task<(string PaymentIntentId, string ClientSecret)> CreateBidPaymentAsync(decimal amount, string connectedAccountId)
    {
        // Same pattern: capture on platform, transfer to seller on escrow release
        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)(amount * 100),
            Currency = "eur",
            PaymentMethodTypes = new List<string> { "card" },
        };

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(options);
        return (intent.Id, intent.ClientSecret);
    }

    public async Task<string> CreateLoginLinkAsync(string accountId)
    {
        var service = new AccountLoginLinkService();
        var link = await service.CreateAsync(accountId);
        return link.Url;
    }

    public async Task<bool> RefundPaymentAsync(string paymentIntentId, decimal? amount = null)
    {
        try
        {
            var options = new RefundCreateOptions
            {
                PaymentIntent = paymentIntentId,
                Amount = amount.HasValue ? (long)(amount.Value * 100) : null
            };

            var service = new RefundService();
            await service.CreateAsync(options);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
