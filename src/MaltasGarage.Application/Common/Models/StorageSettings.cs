using System.ComponentModel.DataAnnotations;

namespace MaltasGarage.Application.Common.Models;

public enum StorageProvider
{
    /// <summary>Files are written to wwwroot/uploads on the web server's disk.</summary>
    Local,

    /// <summary>Files are stored in an Azure Blob Storage container.</summary>
    AzureBlob
}

public class StorageSettings : IValidatableObject
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.Local;

    /// <summary>Required only when <see cref="Provider"/> is <see cref="StorageProvider.AzureBlob"/>.</summary>
    public string? AzureBlobConnectionString { get; set; }

    public string AzureBlobContainer { get; set; } = "listings";

    /// <summary>
    /// Local provider only: folder for uploads. Empty means wwwroot/uploads. On Azure App
    /// Service use a path on the persistent /home volume, e.g. /home/data/uploads.
    /// </summary>
    public string? LocalRootPath { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == StorageProvider.AzureBlob && string.IsNullOrWhiteSpace(AzureBlobConnectionString))
            yield return new ValidationResult(
                "Storage:AzureBlobConnectionString is required when Storage:Provider is AzureBlob.",
                new[] { nameof(AzureBlobConnectionString) });
    }
}
