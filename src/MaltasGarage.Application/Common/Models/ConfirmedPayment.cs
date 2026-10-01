namespace MaltasGarage.Application.Common.Models;

/// <summary>
/// A PaymentIntent that Stripe reports as succeeded, with the amount Stripe actually charged and
/// the metadata this server attached when it created the intent. Pages that finish a payment
/// trust these values, never the amount or ids in their own query string, which the buyer controls.
/// </summary>
public record ConfirmedPayment(string PaymentIntentId, decimal Amount, IReadOnlyDictionary<string, string> Metadata)
{
    public string? Get(string key) => Metadata.TryGetValue(key, out var value) ? value : null;

    /// <summary>True when this server created the payment for this purpose, subject and payer.</summary>
    public bool IsFor(string purpose, Guid subjectId, Guid payerId) =>
        Get(PaymentMetadata.Purpose) == purpose &&
        Get(PaymentMetadata.SubjectId) == subjectId.ToString() &&
        Get(PaymentMetadata.PayerId) == payerId.ToString();
}

/// <summary>Metadata keys and purposes written on every PaymentIntent the app creates.</summary>
public static class PaymentMetadata
{
    public const string Purpose = "purpose";
    public const string SubjectId = "subject_id"; // listing id, or bundle offer id
    public const string PayerId = "payer_id";
    public const string OfferId = "offer_id";

    public const string BuyNow = "buy_now";
    public const string Bid = "bid";
    public const string Bundle = "bundle";

    public static Dictionary<string, string> For(string purpose, Guid subjectId, Guid payerId, Guid? offerId = null)
    {
        var metadata = new Dictionary<string, string>
        {
            [Purpose] = purpose,
            [SubjectId] = subjectId.ToString(),
            [PayerId] = payerId.ToString()
        };
        if (offerId.HasValue)
            metadata[OfferId] = offerId.Value.ToString();
        return metadata;
    }
}
