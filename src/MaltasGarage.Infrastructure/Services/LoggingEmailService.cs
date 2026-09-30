using MaltasGarage.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace MaltasGarage.Infrastructure.Services;

/// <summary>
/// Default email sender for local development and the public demo: nothing leaves the app,
/// every message is written to the log instead.
/// </summary>
public class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody, string? replyTo = null)
    {
        _logger.LogInformation(
            "Email not sent (Email:Provider is Log). To: {ToName} <{ToEmail}>, Subject: {Subject}, ReplyTo: {ReplyTo}",
            toName, toEmail, subject, replyTo ?? "-");
        return Task.CompletedTask;
    }
}
