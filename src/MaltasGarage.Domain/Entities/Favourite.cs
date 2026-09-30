using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class Favourite : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid ListingId { get; set; }

    // Navigation
    public UserProfile User { get; set; } = null!;
    public Listing Listing { get; set; } = null!;
}
