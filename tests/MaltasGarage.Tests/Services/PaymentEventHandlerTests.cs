using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Tests.Services;

/// <summary>Stripe delivers webhook events more than once and out of order; only a pending payment may change.</summary>
public class PaymentEventHandlerTests
{
    [Fact]
    public async Task PaymentSucceeded_ForAPendingPayment_CapturesItAndMarksTheOrderPaid()
    {
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Pending, paymentStatus: PaymentStatus.Pending);
        var handler = new PaymentEventHandler(ctx, TimeProvider.System);

        Assert.True(await handler.PaymentSucceededAsync(order.Payment!.StripePaymentIntentId!));

        var updated = await ctx.Orders.Include(o => o.Payment).FirstAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentStatus.Captured, updated.Payment!.Status);
        Assert.Equal(OrderStatus.Paid, updated.Status);
    }

    [Theory]
    [InlineData(PaymentStatus.Refunded, OrderStatus.Refunded)]
    [InlineData(PaymentStatus.Released, OrderStatus.Completed)]
    [InlineData(PaymentStatus.PartialRefund, OrderStatus.Completed)]
    [InlineData(PaymentStatus.Captured, OrderStatus.Shipped)]
    public async Task PaymentSucceeded_RedeliveredForAnOrderThatMovedOn_ChangesNothing(PaymentStatus paymentStatus, OrderStatus orderStatus)
    {
        // Regression: a redelivered event used to move a refunded order back to Paid, and the
        // auto-release then paid the seller for money that had been returned
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx, orderStatus, paymentStatus: paymentStatus);
        var paidAt = order.PaidAt;
        var handler = new PaymentEventHandler(ctx, TimeProvider.System);

        Assert.False(await handler.PaymentSucceededAsync(order.Payment!.StripePaymentIntentId!));

        var unchanged = await ctx.Orders.Include(o => o.Payment).FirstAsync(o => o.Id == order.Id);
        Assert.Equal(paymentStatus, unchanged.Payment!.Status);
        Assert.Equal(orderStatus, unchanged.Status);
        Assert.Equal(paidAt, unchanged.PaidAt);
    }

    [Fact]
    public async Task PaymentSucceeded_ForAnUnknownPaymentIntent_IsIgnored()
    {
        var ctx = TestDbContextFactory.Create();
        var handler = new PaymentEventHandler(ctx, TimeProvider.System);

        Assert.False(await handler.PaymentSucceededAsync("pi_unknown"));
    }

    [Fact]
    public async Task SellerAccountReady_MarksOnboardingComplete()
    {
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx);
        var handler = new PaymentEventHandler(ctx, TimeProvider.System);

        await handler.SellerAccountReadyAsync("acct_test");

        Assert.True((await ctx.UserProfiles.FindAsync(order.SellerId))!.StripeOnboardingComplete);
    }
}
