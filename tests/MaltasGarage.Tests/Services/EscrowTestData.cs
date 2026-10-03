using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;

namespace MaltasGarage.Tests.Services;

/// <summary>Seeds orders whose money is held in escrow: a seller with a Stripe account, a buyer and a captured payment.</summary>
public static class EscrowTestData
{
    public static async Task<Order> SeedOrderAsync(
        ApplicationDbContext ctx,
        OrderStatus status = OrderStatus.Paid,
        DeliveryMethod delivery = DeliveryMethod.MaltaPost,
        PaymentStatus paymentStatus = PaymentStatus.Captured,
        decimal price = 100m)
    {
        var seller = new UserProfile { Id = Guid.NewGuid(), UserId = $"seller-{Guid.NewGuid():N}", StripeAccountId = "acct_test" };
        var buyer = new UserProfile { Id = Guid.NewGuid(), UserId = $"buyer-{Guid.NewGuid():N}" };
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = seller.Id,
            CategoryId = Guid.NewGuid(),
            Title = "Escrow listing",
            MinPrice = price / 2,
            DesiredPrice = price,
            CurrentPrice = price,
            Status = ListingStatus.Sold,
            SellByDate = DateTime.UtcNow.AddDays(10),
            AuctionStartDate = DateTime.UtcNow.AddDays(3)
        };

        var fee = Domain.Common.PlatformFee.Calculate(price);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            FinalPrice = price,
            PlatformFee = fee,
            SellerPayout = price - fee,
            DeliveryMethod = delivery,
            Status = status,
            PaidAt = DateTime.UtcNow.AddDays(-1)
        };
        order.Payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            StripePaymentIntentId = $"pi_{order.Id:N}",
            Amount = price,
            Status = paymentStatus
        };

        ctx.UserProfiles.AddRange(seller, buyer);
        ctx.Listings.Add(listing);
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();
        return order;
    }
}
