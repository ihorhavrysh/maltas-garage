using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Domain.Entities;

public class Shipment : BaseEntity
{
    public Guid OrderId { get; set; }

    public DeliveryMethod Method { get; set; }
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }

    public ShipmentStatus Status { get; set; } = ShipmentStatus.AwaitingShipment;

    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? DeliveryDeadline { get; set; } // ShippedAt + 7 days

    // Navigation properties
    public Order Order { get; set; } = null!;
}
