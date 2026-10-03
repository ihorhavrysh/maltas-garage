using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Domain.Entities;

public class Dispute : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid OpenedById { get; set; }

    public DisputeReason Reason { get; set; }
    public string? Description { get; set; }

    public DisputeStatus Status { get; set; } = DisputeStatus.Open;
    public OrderStatus PreviousOrderStatus { get; set; }
    public DisputeResolution? Resolution { get; set; }
    public decimal? PartialRefundAmount { get; set; }
    public string? AdminNotes { get; set; }

    public DateTime? ResolvedAt { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public UserProfile OpenedBy { get; set; } = null!;
    public ICollection<DisputeAttachment> Attachments { get; set; } = new List<DisputeAttachment>();
}
