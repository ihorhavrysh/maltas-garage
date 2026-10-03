using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Domain.Entities;

public class Payment : BaseEntity
{
    public Guid OrderId { get; set; }

    public string? StripePaymentIntentId { get; set; }
    public string? StripeTransferId { get; set; }   // set once the seller is paid; a second release is skipped

    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTime? CapturedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
}
