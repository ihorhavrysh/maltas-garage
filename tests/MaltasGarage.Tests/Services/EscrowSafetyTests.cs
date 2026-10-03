using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Services;
using MaltasGarage.Tests.Stubs;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Tests.Services;

/// <summary>
/// Money must move once and only from the right state: no second refund, no payout during a
/// dispute, no negative payout, and one failing transfer must not hold up everyone else's.
/// </summary>
public class EscrowSafetyTests
{
    private static OrderService CreateService(Infrastructure.Data.ApplicationDbContext ctx, RecordingPaymentService payment)
        => new(ctx, new NoOpMessagingService(), new NoOpEmailNotificationService(), payment, TimeProvider.System);

    // ── Refunds ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderStatus.Refunded)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Pending)]
    public async Task RefundBuyer_WhenMoneyIsNoLongerHeld_ThrowsAndRefundsNothing(OrderStatus status)
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var order = await EscrowTestData.SeedOrderAsync(ctx, status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(ctx, payment).RefundBuyerAsync(order.Id));

        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public async Task RefundBuyer_Twice_RefundsOnce()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Disputed);
        var service = CreateService(ctx, payment);

        await service.RefundBuyerAsync(order.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefundBuyerAsync(order.Id));

        Assert.Single(payment.Refunds);
        Assert.Equal(OrderStatus.Refunded, (await ctx.Orders.FindAsync(order.Id))!.Status);
    }

    [Fact]
    public async Task PartialRefund_LargerThanSellerPayout_Throws()
    {
        // 100 EUR order: fee 10, seller payout 90. A 95 EUR refund would make the payout negative
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Disputed);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(ctx, payment).RefundBuyerPartialAsync(order.Id, 95m));

        Assert.Empty(payment.Refunds);
        Assert.Empty(payment.TransferGroups);
    }

    [Fact]
    public async Task PartialRefund_RefundsBuyerAndPaysSellerTheRest()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Disputed);

        await CreateService(ctx, payment).RefundBuyerPartialAsync(order.Id, 30m);

        var updated = await ctx.Orders.Include(o => o.Payment).FirstAsync(o => o.Id == order.Id);
        Assert.Equal((order.Payment!.StripePaymentIntentId!, (decimal?)30m), Assert.Single(payment.Refunds));
        Assert.Equal(order.Id.ToString(), Assert.Single(payment.TransferGroups));
        Assert.Equal(60m, updated.SellerPayout);
        Assert.Equal(OrderStatus.Completed, updated.Status);
        Assert.Equal(PaymentStatus.PartialRefund, updated.Payment!.Status);
    }

    [Fact]
    public async Task PartialRefund_WhenTransferFails_ThrowsAndLeavesTheOrderOpen()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Disputed);
        payment.FailingTransferGroups.Add(order.Id.ToString());

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(ctx, payment).RefundBuyerPartialAsync(order.Id, 30m));

        ctx.ChangeTracker.Clear();
        var unchanged = await ctx.Orders.Include(o => o.Payment).FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Disputed, unchanged.Status);
        Assert.Equal(PaymentStatus.Captured, unchanged.Payment!.Status);
        Assert.Equal(90m, unchanged.SellerPayout);
    }

    // ── Auto-release ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AutoRelease_OneFailingTransfer_DoesNotStopTheOthers()
    {
        var ctx = TestDbContextFactory.Create();
        var payment = new RecordingPaymentService();
        var failing = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Paid, DeliveryMethod.HandToHand);
        var healthy = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Paid, DeliveryMethod.HandToHand);
        payment.FailingTransferGroups.Add(failing.Id.ToString());

        // Both were paid a day ago; hand-to-hand auto-release is due after 7 days
        var released = await CreateService(ctx, payment).AutoReleaseDueEscrowAsync(DateTime.UtcNow.AddDays(10));

        Assert.Equal(1, released);
        ctx.ChangeTracker.Clear();
        Assert.Equal(OrderStatus.Paid, (await ctx.Orders.FindAsync(failing.Id))!.Status);
        Assert.Equal(OrderStatus.Completed, (await ctx.Orders.FindAsync(healthy.Id))!.Status);
    }

    // ── Shipping ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderStatus.Disputed, DeliveryMethod.MaltaPost)]   // would start auto-release during a dispute
    [InlineData(OrderStatus.Shipped, DeliveryMethod.MaltaPost)]    // already shipped
    [InlineData(OrderStatus.Refunded, DeliveryMethod.MaltaPost)]
    [InlineData(OrderStatus.Paid, DeliveryMethod.HandToHand)]      // nothing to ship
    public async Task MarkShipped_InTheWrongState_Throws(OrderStatus status, DeliveryMethod delivery)
    {
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx, status, delivery);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(ctx, new RecordingPaymentService()).MarkShippedAsync(order.Id, order.SellerId, "RR123456789MT"));

        Assert.Equal(status, (await ctx.Orders.FindAsync(order.Id))!.Status);
        Assert.False(await ctx.Shipments.AnyAsync(s => s.OrderId == order.Id));
    }

    [Fact]
    public async Task MarkShipped_BySomeoneOtherThanTheSeller_Throws()
    {
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(ctx, new RecordingPaymentService()).MarkShippedAsync(order.Id, order.BuyerId, "RR123456789MT"));
    }

    [Fact]
    public async Task MarkShipped_PaidMaltaPostOrder_CreatesShipment()
    {
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx);

        await CreateService(ctx, new RecordingPaymentService()).MarkShippedAsync(order.Id, order.SellerId, " RR123456789MT ");

        var shipment = await ctx.Shipments.SingleAsync(s => s.OrderId == order.Id);
        Assert.Equal("RR123456789MT", shipment.TrackingNumber);
        Assert.Equal(OrderStatus.Shipped, (await ctx.Orders.FindAsync(order.Id))!.Status);
    }
}
