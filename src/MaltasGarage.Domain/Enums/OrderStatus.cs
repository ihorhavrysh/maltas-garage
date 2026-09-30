namespace MaltasGarage.Domain.Enums;

public enum OrderStatus
{
    Pending = 0,
    Paid = 1,
    Shipped = 2,
    Delivered = 3,
    Completed = 4,
    Disputed = 5,
    Refunded = 6,
    Cancelled = 7
}
