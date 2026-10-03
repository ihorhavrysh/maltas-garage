namespace MaltasGarage.Domain.Enums;

public enum PaymentStatus
{
    Pending = 0,
    Captured = 1,   // charged and held by the platform (escrow) until release or refund
    Released = 2,
    Refunded = 3,
    PartialRefund = 4
}
