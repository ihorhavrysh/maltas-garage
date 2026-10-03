namespace MaltasGarage.Application.Common.Models;

/// <summary>
/// The captured Stripe payment an order is created with, and the accepted price offer it
/// completes (if any). The order, its payment and the offer are then saved together.
/// </summary>
public record OrderPayment(string PaymentIntentId, Guid? OfferId = null);
