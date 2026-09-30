using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid ListingId { get; set; }
    public decimal Price { get; set; }

    public Order Order { get; set; } = null!;
    public Listing Listing { get; set; } = null!;
}
