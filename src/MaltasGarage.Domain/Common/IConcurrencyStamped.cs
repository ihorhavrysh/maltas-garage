namespace MaltasGarage.Domain.Common;

/// <summary>
/// Entities whose state transitions can be raced (a background sweep and a page request both
/// closing the same auction, say). The stamp is an optimistic concurrency token that changes on
/// every save, so the second writer gets a concurrency conflict instead of applying the
/// transition twice.
/// </summary>
public interface IConcurrencyStamped
{
    Guid ConcurrencyStamp { get; set; }
}
