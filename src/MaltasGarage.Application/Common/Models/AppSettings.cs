using System.ComponentModel.DataAnnotations;

namespace MaltasGarage.Application.Common.Models;

public class AppSettings
{
    public const string SectionName = "App";

    /// <summary>Public URL of the site, used to build absolute links in emails.</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Address that receives contact-form messages.</summary>
    [Required, EmailAddress]
    public string SupportEmail { get; set; } = string.Empty;

    /// <summary>Optional Google Analytics measurement id (G-...). No tracking when empty.</summary>
    public string? GoogleAnalyticsId { get; set; }

    /// <summary>
    /// How often the background sweep catches up on time-based changes while the app is awake.
    /// Pages evaluate listings on read, so this only affects what nobody has looked at.
    /// </summary>
    [Range(1, 1440)]
    public int SweepIntervalMinutes { get; set; } = 15;
}
