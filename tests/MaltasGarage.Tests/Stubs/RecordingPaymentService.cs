namespace MaltasGarage.Tests.Stubs;

/// <summary>
/// Payment stub that records every transfer and refund request, and can be told to fail the
/// transfer for chosen orders (the transfer group is the order id).
/// </summary>
public class RecordingPaymentService : NoOpPaymentService
{
    public List<string> TransferGroups { get; } = new();
    public List<(string PaymentIntentId, decimal? Amount)> Refunds { get; } = new();
    public HashSet<string> FailingTransferGroups { get; } = new();

    public override Task<string?> CreateTransferAsync(decimal sellerPayout, string connectedAccountId, string transferGroup, string? paymentIntentId = null)
    {
        TransferGroups.Add(transferGroup);
        return Task.FromResult(FailingTransferGroups.Contains(transferGroup) ? null : (string?)$"tr_{transferGroup}");
    }

    public override Task<bool> RefundPaymentAsync(string paymentIntentId, decimal? amount = null)
    {
        Refunds.Add((paymentIntentId, amount));
        return Task.FromResult(true);
    }
}
