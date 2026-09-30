namespace MaltasGarage.Domain.Enums;

public enum PaymentStatus
{
    Pending = 0,
    Captured = 1,   // legacy / auction payments (auto-capture)
    Released = 2,
    Refunded = 3,
    PartialRefund = 4,
    Authorized = 5  // manual capture: hold placed, not yet captured
}
