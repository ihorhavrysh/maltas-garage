using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class DisputeAttachment : BaseEntity
{
    public Guid DisputeId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    public Dispute Dispute { get; set; } = null!;
}
