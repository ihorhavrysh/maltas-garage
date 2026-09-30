using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class Review : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }

    public int Rating { get; set; } // 1-5
    public string? Comment { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public UserProfile FromUser { get; set; } = null!;
    public UserProfile ToUser { get; set; } = null!;
}
