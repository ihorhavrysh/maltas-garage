namespace MaltasGarage.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody, string? replyTo = null);
}
