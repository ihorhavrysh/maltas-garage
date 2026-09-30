namespace MaltasGarage.Application.Common.Interfaces;

/// <summary>
/// Applies the time-based transitions of the marketplace: auctions opening, listings expiring,
/// auctions closing with a winner, offers expiring and escrow being released.
///
/// Correctness never depends on a timer having fired. Pages that act on a listing call
/// <see cref="EnsureCurrentAsync"/> first, so a listing past its end time is closed the moment
/// anyone looks at it. <see cref="SweepAsync"/> catches up on everything else when the app
/// starts (the free App Service tier sleeps when idle) and then periodically while it is awake.
/// Every transition is idempotent: if a sweep and a request race, one wins and the other skips.
/// </summary>
public interface IListingLifecycleService
{
    /// <summary>Brings one listing up to date. Cheap when nothing is due.</summary>
    Task EnsureCurrentAsync(Guid listingId, CancellationToken cancellationToken = default);

    /// <summary>Applies every transition that is due right now.</summary>
    Task<LifecycleSweepResult> SweepAsync(CancellationToken cancellationToken = default);
}

public record LifecycleSweepResult(
    int AuctionsOpened,
    int ListingsExpired,
    int AuctionsClosed,
    int OffersExpired,
    int EscrowsReleased)
{
    public int Total => AuctionsOpened + ListingsExpired + AuctionsClosed + OffersExpired + EscrowsReleased;
}
