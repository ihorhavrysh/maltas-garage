using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Domain.Entities;

public class SystemMessage : BaseEntity
{
    public Guid UserId { get; set; }
    public SystemMessageType Type { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? Link { get; set; }
    public bool IsRead { get; set; }

    public UserProfile User { get; set; } = null!;
}
