using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;

namespace MaltasGarage.Application.Common.Interfaces;

public interface IDisputeService
{
    Task<Dispute> OpenDisputeAsync(Guid orderId, Guid openedById, DisputeReason reason, string? description);
    Task<Dispute?> GetDisputeByOrderAsync(Guid orderId);
    Task MarkUnderReviewAsync(Guid orderId);
    Task ResolveDisputeAsync(Guid disputeId, DisputeResolution resolution, string? adminNotes, decimal? partialRefundAmount = null);
    Task WithdrawDisputeAsync(Guid orderId, Guid userId);
}
