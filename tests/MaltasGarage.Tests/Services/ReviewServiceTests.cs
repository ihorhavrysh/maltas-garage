using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Services;

namespace MaltasGarage.Tests.Services;

public class ReviewServiceTests
{
    [Fact]
    public async Task Review_OfCompletedOrder_UpdatesTheOtherPartysRating()
    {
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Completed);
        var service = new ReviewService(ctx, TimeProvider.System);

        await service.LeaveReviewAsync(order.Id, order.BuyerId, 4, "Fine");
        await service.LeaveReviewAsync(order.Id, order.SellerId, 5, null);

        var seller = await ctx.UserProfiles.FindAsync(order.SellerId);
        Assert.Equal(4m, seller!.Rating);
        Assert.Equal(1, seller.TotalReviews);
        Assert.Equal(5m, (await ctx.UserProfiles.FindAsync(order.BuyerId))!.Rating);
    }

    [Fact]
    public async Task Review_Twice_Throws()
    {
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Completed);
        var service = new ReviewService(ctx, TimeProvider.System);

        await service.LeaveReviewAsync(order.Id, order.BuyerId, 4, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LeaveReviewAsync(order.Id, order.BuyerId, 1, null));
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Disputed)]
    public async Task Review_BeforeCompletion_Throws(OrderStatus status)
    {
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx, status);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ReviewService(ctx, TimeProvider.System).LeaveReviewAsync(order.Id, order.BuyerId, 5, null));
    }

    [Fact]
    public async Task Review_BySomeoneElse_Throws()
    {
        var ctx = TestDbContextFactory.Create();
        var order = await EscrowTestData.SeedOrderAsync(ctx, OrderStatus.Completed);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ReviewService(ctx, TimeProvider.System).LeaveReviewAsync(order.Id, Guid.NewGuid(), 5, null));
    }
}
