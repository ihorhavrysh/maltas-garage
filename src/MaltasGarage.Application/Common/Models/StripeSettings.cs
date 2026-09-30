using System.ComponentModel.DataAnnotations;

namespace MaltasGarage.Application.Common.Models;

public class StripeSettings
{
    public const string SectionName = "Stripe";

    [Required]
    public string SecretKey { get; set; } = string.Empty;

    [Required]
    public string PublishableKey { get; set; } = string.Empty;

    [Required]
    public string WebhookSecret { get; set; } = string.Empty;
}
