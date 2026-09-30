using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Domain.Entities;

public class Payment : BaseEntity
{
    public Guid OrderId { get; set; }

    public string? StripePaymentIntentId { get; set; }
    public string? StripeTransferId { get; set; }

    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTime? CapturedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DateTime? EscrowReleaseDate { get; set; } // When to auto-release

    // Navigation properties
    public Order Order { get; set; } = null!;
}
