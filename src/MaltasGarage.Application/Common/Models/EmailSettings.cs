using System.ComponentModel.DataAnnotations;

namespace MaltasGarage.Application.Common.Models;

public enum EmailProvider
{
    /// <summary>Emails are written to the application log and never sent.</summary>
    Log,

    /// <summary>Emails are sent through the Resend API.</summary>
    Resend
}

public class EmailSettings : IValidatableObject
{
    public const string SectionName = "Email";

    public EmailProvider Provider { get; set; } = EmailProvider.Log;

    [Required]
    public string FromEmail { get; set; } = string.Empty;

    [Required]
    public string FromName { get; set; } = string.Empty;

    /// <summary>Required only when <see cref="Provider"/> is <see cref="EmailProvider.Resend"/>.</summary>
    public string? ResendApiKey { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == EmailProvider.Resend && string.IsNullOrWhiteSpace(ResendApiKey))
            yield return new ValidationResult(
                "Email:ResendApiKey is required when Email:Provider is Resend.",
                new[] { nameof(ResendApiKey) });
    }
}
