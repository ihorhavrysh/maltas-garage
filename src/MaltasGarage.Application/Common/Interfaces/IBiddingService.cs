using MaltasGarage.Domain.Entities;

namespace MaltasGarage.Application.Common.Interfaces;

public interface IBiddingService
{
    Task<BidResult> PlaceBidAsync(Guid listingId, Guid bidderId, decimal amount, string? paymentIntentId = null);
}

public class BidResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public decimal? NewHighBid { get; set; }
}
