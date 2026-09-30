using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MaltasGarage.Infrastructure.Services;

public class ResendEmailService : IEmailService
{
    public const string HttpClientName = "Resend";

    private readonly EmailSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        IOptions<EmailSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<ResendEmailService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, string? replyTo = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            var payload = replyTo != null
                ? (object)new
                {
                    from = $"{_settings.FromName} <{_settings.FromEmail}>",
                    to = new[] { $"{toName} <{toEmail}>" },
                    reply_to = replyTo,
                    subject,
                    html = htmlBody
                }
                : new
                {
                    from = $"{_settings.FromName} <{_settings.FromEmail}>",
                    to = new[] { $"{toName} <{toEmail}>" },
                    subject,
                    html = htmlBody
                };

            // The API key is attached per request from options, so it follows whatever
            // configuration source is active instead of being captured at startup.
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ResendApiKey);

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Resend API error {StatusCode}: {Error}", response.StatusCode, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
        }
    }
}
