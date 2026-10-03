using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Services;
using MaltasGarage.Tests.Stubs;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Tests.Services;

public class DisputeServiceTests
{
    private static (DisputeService Disputes, RecordingPaymentService Payment) CreateServices(Infrastructure.Data.ApplicationDbContext ctx)
    {
        var payment = new RecordingPaymentService();
        var messaging = new NoOpMessagingService();
        var email = new NoOpEmailNotificationService();
        var orders = new OrderService(ctx, messaging, email, payment, TimeProvider.System);
        return (new DisputeService(ctx, orders, messaging, email), payment);
    }

    private static async Task<Order> SeedDisputedOrderAsync(Infrastructure.Data.ApplicationDbContext ctx, string status = "Open")
    {
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Disputed);
        ctx.Disputes.Add(new Dispute
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            OpenedById = order.BuyerId,
            Reason = DisputeReason.ItemNotAsDescribed,
            Status = status,
            PreviousOrderStatus = OrderStatus.Shipped
        });
        await ctx.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task OpenDispute_ByTheBuyer_MovesOrderToDisputedAndRemembersTheStatus()
    {
        var ctx = TestDbContextFactory.Create();
        var (disputes, _) = CreateServices(ctx);
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Shipped);

        var dispute = await disputes.OpenDisputeAsync(order.Id, order.BuyerId, DisputeReason.ItemNotReceived, "Never arrived");

        Assert.Equal("Open", dispute.Status);
        Assert.Equal(OrderStatus.Shipped, dispute.PreviousOrderStatus);
        Assert.Equal(OrderStatus.Disputed, (await ctx.Orders.FindAsync(order.Id))!.Status);
    }

    [Fact]
    public async Task OpenDispute_ByTheSeller_Throws()
    {
        var ctx = TestDbContextFactory.Create();
        var (disputes, _) = CreateServices(ctx);
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Shipped);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            disputes.OpenDisputeAsync(order.Id, order.SellerId, DisputeReason.Other, null));
    }

    [Fact]
    public async Task Withdraw_RestoresThePreviousOrderStatus()
    {
        var ctx = TestDbContextFactory.Create();
        var (disputes, _) = CreateServices(ctx);
        var order = await SeedDisputedOrderAsync(ctx);

        await disputes.WithdrawDisputeAsync(order.Id, order.BuyerId);

        Assert.Equal(OrderStatus.Shipped, (await ctx.Orders.FindAsync(order.Id))!.Status);
        Assert.Equal("Withdrawn", (await ctx.Disputes.SingleAsync(d => d.OrderId == order.Id)).Status);
    }

    [Fact]
    public async Task Resolve_Twice_RefundsTheBuyerOnce()
    {
        // Regression: a second POST of the same resolution used to refund again
        var ctx = TestDbContextFactory.Create();
        var (disputes, payment) = CreateServices(ctx);
        var order = await SeedDisputedOrderAsync(ctx);
        var disputeId = (await ctx.Disputes.SingleAsync(d => d.OrderId == order.Id)).Id;

        await disputes.ResolveDisputeAsync(disputeId, DisputeResolution.PartialRefund, null, 20m);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            disputes.ResolveDisputeAsync(disputeId, DisputeResolution.PartialRefund, null, 20m));

        Assert.Single(payment.Refunds);
        Assert.Single(payment.TransferGroups);
    }

    [Theory]
    [InlineData("Resolved")]
    [InlineData("Withdrawn")]
    public async Task Resolve_AClosedDispute_ThrowsAndMovesNoMoney(string status)
    {
        var ctx = TestDbContextFactory.Create();
        var (disputes, payment) = CreateServices(ctx);
        var order = await SeedDisputedOrderAsync(ctx, status);
        var disputeId = (await ctx.Disputes.SingleAsync(d => d.OrderId == order.Id)).Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            disputes.ResolveDisputeAsync(disputeId, DisputeResolution.RefundBuyer, null));

        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public async Task Resolve_RefundBuyer_RefundsAndRelistsTheItem()
    {
        var ctx = TestDbContextFactory.Create();
        var (disputes, payment) = CreateServices(ctx);
        var order = await SeedDisputedOrderAsync(ctx);
        var disputeId = (await ctx.Disputes.SingleAsync(d => d.OrderId == order.Id)).Id;

        await disputes.ResolveDisputeAsync(disputeId, DisputeResolution.RefundBuyer, "Item damaged");

        Assert.Single(payment.Refunds);
        Assert.Equal(OrderStatus.Refunded, (await ctx.Orders.FindAsync(order.Id))!.Status);
        Assert.Equal(ListingStatus.Active, (await ctx.Listings.FindAsync(order.ListingId))!.Status);
        Assert.Equal("Resolved", (await ctx.Disputes.FindAsync(disputeId))!.Status);
    }

    [Fact]
    public async Task Resolve_PaySeller_ReleasesEscrow()
    {
        var ctx = TestDbContextFactory.Create();
        var (disputes, payment) = CreateServices(ctx);
        var order = await SeedDisputedOrderAsync(ctx);
        var disputeId = (await ctx.Disputes.SingleAsync(d => d.OrderId == order.Id)).Id;

        await disputes.ResolveDisputeAsync(disputeId, DisputeResolution.PaySeller, null);

        Assert.Empty(payment.Refunds);
        Assert.Equal(order.Id.ToString(), Assert.Single(payment.TransferGroups));
        Assert.Equal(OrderStatus.Completed, (await ctx.Orders.FindAsync(order.Id))!.Status);
    }
}
