using System.Net;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Tests.Services;

public class ResendEmailServiceTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { BaseAddress = new Uri("https://api.resend.com/") };
    }

    [Fact]
    public async Task SendAsync_AttachesApiKeyFromOptionsToEachRequest()
    {
        var handler = new RecordingHandler();
        var settings = Options.Create(new EmailSettings
        {
            Provider = EmailProvider.Resend,
            FromEmail = "noreply@example.com",
            FromName = "Malta's Garage",
            ResendApiKey = "re_test_key"
        });
        var service = new ResendEmailService(settings, new StubHttpClientFactory(handler), NullLogger<ResendEmailService>.Instance);

        await service.SendAsync("buyer@example.com", "Buyer", "Subject", "<p>Hi</p>");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(new Uri("https://api.resend.com/emails"), handler.LastRequest!.RequestUri);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("re_test_key", handler.LastRequest.Headers.Authorization?.Parameter);
    }
}
