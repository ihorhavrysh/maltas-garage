using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class HandoverCode : BaseEntity
{
    public Guid OrderId { get; set; }

    public string Code { get; set; } = string.Empty; // 6-digit OTP
    public string? QrCodeUrl { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
}
