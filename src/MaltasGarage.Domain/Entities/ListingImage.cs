using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class ListingImage : BaseEntity
{
    public Guid ListingId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public int SortOrder { get; set; }

    // Navigation properties
    public Listing Listing { get; set; } = null!;
}
