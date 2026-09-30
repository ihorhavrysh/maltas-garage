using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Services;
using MaltasGarage.Tests.Stubs;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Tests.Services;

public class OrderServiceTests
{
    private static readonly NoOpMessagingService _messaging = new();
    private static readonly NoOpEmailNotificationService _emailNotifications = new();
    private static readonly NoOpPaymentService _payment = new();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Listing MakeListing(
        Guid? sellerId = null,
        ListingStatus status = ListingStatus.Active,
        decimal price = 100m)
    {
        return new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = sellerId ?? Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Title = "Test Listing",
            MinPrice = price * 0.5m,
            DesiredPrice = price,
            CurrentPrice = price,
            Status = status,
            SellByDate = DateTime.UtcNow.AddDays(30),
            AuctionStartDate = DateTime.UtcNow.AddDays(23)
        };
    }

    // ── Fee calculation (pure logic, no DB needed) ───────────────────────────

    [Theory]
    [InlineData(9,    1.00)]  // 10% = €0.90 → rounded up to minimum €1
    [InlineData(10,   1.00)]  // 10% = €1.00 → exactly minimum
    [InlineData(100,  10.00)] // 10% = €10
    [InlineData(250,  25.00)] // 10% = €25
    [InlineData(0.50, 1.00)]  // tiny price → minimum applies
    public void CalculatePlatformFee_ReturnsCorrectAmount(decimal price, decimal expectedFee)
    {
        var service = new OrderService(TestDbContextFactory.Create(), _messaging, _emailNotifications, _payment);

        var fee = service.CalculatePlatformFee(price);

        Assert.Equal(expectedFee, fee);
    }

    // ── CreateOrderAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrder_MarksListingSoldAndReturnsOrderWithCorrectFees()
    {
        var ctx = TestDbContextFactory.Create();
        var sellerId = Guid.NewGuid();
        var listing = MakeListing(sellerId, price: 100m);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var buyerId = Guid.NewGuid();
        var service = new OrderService(ctx, _messaging, _emailNotifications, _payment);

        var order = await service.CreateOrderAsync(listing.Id, buyerId, 100m, DeliveryMethod.MaltaPost);

        // Order details correct
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(buyerId, order.BuyerId);
        Assert.Equal(sellerId, order.SellerId);
        Assert.Equal(100m, order.FinalPrice);
        Assert.Equal(10m, order.PlatformFee);
        Assert.Equal(90m, order.SellerPayout);

        // Listing marked as Sold
        var updatedListing = await ctx.Listings.FindAsync(listing.Id);
        Assert.Equal(ListingStatus.Sold, updatedListing!.Status);
    }

    [Fact]
    public async Task CreateOrder_AlreadySoldListing_Throws()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = MakeListing(status: ListingStatus.Sold);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var service = new OrderService(ctx, _messaging, _emailNotifications, _payment);

        await Assert.ThrowsAsync<Exception>(() =>
            service.CreateOrderAsync(listing.Id, Guid.NewGuid(), 100m, DeliveryMethod.MaltaPost));
    }

    [Fact]
    public async Task CreateOrder_ListingNotFound_Throws()
    {
        var ctx = TestDbContextFactory.Create();
        var service = new OrderService(ctx, _messaging, _emailNotifications, _payment);

        await Assert.ThrowsAsync<Exception>(() =>
            service.CreateOrderAsync(Guid.NewGuid(), Guid.NewGuid(), 100m, DeliveryMethod.MaltaPost));
    }

    // ── ReleaseEscrowAsync ───────────────────────────────────────────────────

    // Seeds an order whose payment is captured and whose seller has a Stripe account,
    // so the only thing deciding whether escrow can be released is the order status.
    private static async Task<(OrderService Service, Guid OrderId)> SeedPaidOrderAsync(
        MaltasGarage.Infrastructure.Data.ApplicationDbContext ctx, OrderStatus status)
    {
        var seller = new UserProfile { Id = Guid.NewGuid(), UserId = "seller", StripeAccountId = "acct_test" };
        ctx.UserProfiles.Add(seller);
        var listing = MakeListing(seller.Id);
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var service = new OrderService(ctx, _messaging, _emailNotifications, _payment);
        var order = await service.CreateOrderAsync(listing.Id, Guid.NewGuid(), 100m, DeliveryMethod.MaltaPost);

        ctx.Payments.Add(new Payment
        {
            OrderId = order.Id,
            StripePaymentIntentId = "pi_test",
            Amount = order.FinalPrice,
            Status = PaymentStatus.Captured
        });
        order.Status = status;
        await ctx.SaveChangesAsync();

        return (service, order.Id);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Refunded)]
    public async Task ReleaseEscrow_WhenOrderNotReleasable_Throws(OrderStatus status)
    {
        var ctx = TestDbContextFactory.Create();
        var (service, orderId) = await SeedPaidOrderAsync(ctx, status);

        await Assert.ThrowsAsync<Exception>(() => service.ReleaseEscrowAsync(orderId));

        var unchanged = await ctx.Orders.FindAsync(orderId);
        Assert.Equal(status, unchanged!.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]      // hand-to-hand handover
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Disputed)]  // dispute resolved in favour of the seller
    public async Task ReleaseEscrow_WhenOrderPaid_CompletesOrderAndReleasesPayment(OrderStatus status)
    {
        var ctx = TestDbContextFactory.Create();
        var (service, orderId) = await SeedPaidOrderAsync(ctx, status);

        await service.ReleaseEscrowAsync(orderId);

        var updated = await ctx.Orders.Include(o => o.Payment).Include(o => o.Seller)
            .FirstAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Completed, updated.Status);
        Assert.NotNull(updated.CompletedAt);
        Assert.Equal(PaymentStatus.Released, updated.Payment!.Status);
        Assert.NotNull(updated.Payment.ReleasedAt);
        Assert.Equal(1, updated.Seller.TotalSales);
    }

    // ── RefundBuyerAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task RefundBuyer_SetsOrderRefundedAndReleasesListing()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = MakeListing();
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var listingId = listing.Id;
        var service = new OrderService(ctx, _messaging, _emailNotifications, _payment);
        var order = await service.CreateOrderAsync(listingId, Guid.NewGuid(), 100m, DeliveryMethod.HandToHand);

        await service.RefundBuyerAsync(order.Id);

        var updatedOrder = await ctx.Orders.FindAsync(order.Id);
        var updatedListing = await ctx.Listings.FindAsync(listingId);

        Assert.Equal(OrderStatus.Refunded, updatedOrder!.Status);
        Assert.Equal(ListingStatus.Active, updatedListing!.Status);
    }

    // ── UpdateStatusAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStatus_SetsCompletedAtWhenStatusIsCompleted()
    {
        var ctx = TestDbContextFactory.Create();
        var listing = MakeListing();
        ctx.Listings.Add(listing);
        await ctx.SaveChangesAsync();

        var service = new OrderService(ctx, _messaging, _emailNotifications, _payment);
        var order = await service.CreateOrderAsync(listing.Id, Guid.NewGuid(), 100m, DeliveryMethod.MaltaPost);

        Assert.Null(order.CompletedAt);

        await service.UpdateStatusAsync(order.Id, OrderStatus.Completed);

        var updated = await ctx.Orders.FindAsync(order.Id);
        Assert.NotNull(updated!.CompletedAt);
    }
}
