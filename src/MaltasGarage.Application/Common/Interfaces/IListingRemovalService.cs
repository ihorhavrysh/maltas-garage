namespace MaltasGarage.Application.Common.Interfaces;

public interface IListingRemovalService
{
    /// <summary>
    /// Deletes a seller's own listing. One that nothing refers to is removed with its photos; one
    /// that offers, bundles or orders still point at is cancelled instead, so their history stays
    /// readable. Throws <see cref="InvalidOperationException"/> with a message for the user when
    /// the listing cannot be deleted (showcase, has bids, sold).
    /// </summary>
    Task DeleteAsync(Guid listingId, Guid sellerId);
}
