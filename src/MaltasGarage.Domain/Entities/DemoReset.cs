using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

/// <summary>
/// One row per demo (re)seed. The newest CreatedAt tells the app when the next scheduled
/// reset is due, which works even though the free App Service tier sleeps between visits.
/// </summary>
public class DemoReset : BaseEntity
{
    /// <summary>"Seed" for the first seed, "Scheduled" or "Manual" for resets.</summary>
    public string Reason { get; set; } = string.Empty;
}
