namespace MaltasGarage.Application.Common.Interfaces;

public interface IReviewService
{
    /// <summary>
    /// Records the buyer's or the seller's review of a completed order and updates the other
    /// party's average rating. Throws <see cref="InvalidOperationException"/> with a message for
    /// the user when the order is not completed or this side has already reviewed it.
    /// </summary>
    Task LeaveReviewAsync(Guid orderId, Guid fromUserId, int rating, string? comment);
}
