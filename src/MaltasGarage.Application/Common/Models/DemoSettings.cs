using System.ComponentModel.DataAnnotations;

namespace MaltasGarage.Application.Common.Models;

public class DemoSettings : IValidatableObject
{
    public const string SectionName = "Demo";

    /// <summary>
    /// Turns on the public demo: seed data, the demo banner, protected demo accounts,
    /// visitor accounts that can sell straight away, and the demo reset.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// A Stripe test-mode connected account (acct_...) shared by the demo sellers and by
    /// every account a visitor registers, so checkout works without Connect onboarding.
    /// </summary>
    public string? SellerStripeAccountId { get; set; }

    /// <summary>
    /// Visitor data is wiped and the demo reseeded once the last seed is this old.
    /// Checked on startup and by the background sweep, so a sleeping app catches up.
    /// </summary>
    [Range(1, 90)]
    public int ResetIntervalDays { get; set; } = 7;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Enabled && string.IsNullOrWhiteSpace(SellerStripeAccountId))
            yield return new ValidationResult(
                "Demo:SellerStripeAccountId is required when Demo:Enabled is true.",
                new[] { nameof(SellerStripeAccountId) });
    }
}
