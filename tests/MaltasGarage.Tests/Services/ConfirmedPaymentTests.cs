using MaltasGarage.Application.Common.Models;

namespace MaltasGarage.Tests.Services;

// A payment page may only use a Stripe payment that this server created for the same purpose,
// the same listing (or bundle offer) and the same account. These tests pin that rule down.
public class ConfirmedPaymentTests
{
    private static readonly Guid Listing = Guid.NewGuid();
    private static readonly Guid Buyer = Guid.NewGuid();

    private static ConfirmedPayment Payment(Dictionary<string, string> metadata) => new("pi_test", 60m, metadata);

    [Fact]
    public void IsFor_MatchesThePaymentItWasCreatedFor()
    {
        var payment = Payment(PaymentMetadata.For(PaymentMetadata.BuyNow, Listing, Buyer));

        Assert.True(payment.IsFor(PaymentMetadata.BuyNow, Listing, Buyer));
    }

    [Fact]
    public void IsFor_RejectsAPaymentForAnotherListing()
    {
        // e.g. paying for a cheap listing, then completing the purchase of an expensive one
        var payment = Payment(PaymentMetadata.For(PaymentMetadata.BuyNow, Guid.NewGuid(), Buyer));

        Assert.False(payment.IsFor(PaymentMetadata.BuyNow, Listing, Buyer));
    }

    [Fact]
    public void IsFor_RejectsAPaymentMadeByAnotherAccount()
    {
        var payment = Payment(PaymentMetadata.For(PaymentMetadata.BuyNow, Listing, Guid.NewGuid()));

        Assert.False(payment.IsFor(PaymentMetadata.BuyNow, Listing, Buyer));
    }

    [Fact]
    public void IsFor_RejectsAPaymentForAnotherPurpose()
    {
        // A bid payment cannot be turned into a Buy Now purchase of the same listing
        var payment = Payment(PaymentMetadata.For(PaymentMetadata.Bid, Listing, Buyer));

        Assert.False(payment.IsFor(PaymentMetadata.BuyNow, Listing, Buyer));
    }

    [Fact]
    public void IsFor_RejectsAPaymentWithoutMetadata()
    {
        // A PaymentIntent this server did not create carries none of its metadata
        var payment = Payment(new Dictionary<string, string>());

        Assert.False(payment.IsFor(PaymentMetadata.BuyNow, Listing, Buyer));
    }

    [Fact]
    public void For_CarriesTheOfferIdOnlyWhenThereIsOne()
    {
        var offerId = Guid.NewGuid();

        var withOffer = Payment(PaymentMetadata.For(PaymentMetadata.BuyNow, Listing, Buyer, offerId));
        var withoutOffer = Payment(PaymentMetadata.For(PaymentMetadata.BuyNow, Listing, Buyer));

        Assert.Equal(offerId.ToString(), withOffer.Get(PaymentMetadata.OfferId));
        Assert.Null(withoutOffer.Get(PaymentMetadata.OfferId));
    }
}
