using System.ComponentModel.DataAnnotations;

namespace MaltasGarage.Application.Common.Models;

/// <summary>
/// Optional first admin account, created on startup. Kept in User Secrets or App Service
/// settings, never in appsettings.json. Both values or neither.
/// </summary>
public class SeedSettings : IValidatableObject
{
    public const string SectionName = "Seed";

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }

    public bool HasAdmin => !string.IsNullOrWhiteSpace(AdminEmail) && !string.IsNullOrWhiteSpace(AdminPassword);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(AdminEmail) != string.IsNullOrWhiteSpace(AdminPassword))
            yield return new ValidationResult(
                "Set both Seed:AdminEmail and Seed:AdminPassword, or neither.",
                new[] { nameof(AdminEmail), nameof(AdminPassword) });
    }
}
