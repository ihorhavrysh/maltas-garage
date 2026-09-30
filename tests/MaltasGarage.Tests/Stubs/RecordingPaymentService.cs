using MaltasGarage.Application.Common.Interfaces;

namespace MaltasGarage.Tests.Stubs;

/// <summary>Payment stub that records every transfer request (the transfer group is the order id).</summary>
public class RecordingPaymentService : NoOpPaymentService, IPaymentService
{
    public List<string> TransferGroups { get; } = new();

    // Re-implementing IPaymentService makes calls through the interface land here
    public new Task<bool> CreateTransferAsync(decimal sellerPayout, string connectedAccountId, string transferGroup, string? paymentIntentId = null)
    {
        TransferGroups.Add(transferGroup);
        return Task.FromResult(true);
    }
}
