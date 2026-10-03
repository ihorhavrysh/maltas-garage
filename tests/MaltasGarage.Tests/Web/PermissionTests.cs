using System.Net;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Tests.Web;

/// <summary>
/// Every POST checks again what its GET page checked: these requests go straight to the
/// handler, the way a hand-made request would, and must change nothing.
/// </summary>
public class PermissionTests : IClassFixture<MarketplaceWebFactory>
{
    private readonly MarketplaceWebFactory _factory;

    public PermissionTests(MarketplaceWebFactory factory) => _factory = factory;

    private async Task<(Order Order, IdentityUser Buyer, IdentityUser Seller)> SeedOrderAsync(OrderStatus status)
    {
        var buyer = await _factory.CreateUserAsync($"buyer-{Guid.NewGuid():N}@example.test");
        var seller = await _factory.CreateUserAsync($"seller-{Guid.NewGuid():N}@example.test");

        var order = await _factory.WithDbAsync(async db =>
        {
            var category = await db.Categories.FirstAsync();
            var buyerProfile = new UserProfile { Id = Guid.NewGuid(), UserId = buyer.Id, DisplayName = "Buyer" };
            var sellerProfile = new UserProfile { Id = Guid.NewGuid(), UserId = seller.Id, DisplayName = "Seller", StripeAccountId = "acct_test" };
            var listing = new Listing
            {
                Id = Guid.NewGuid(), SellerId = sellerProfile.Id, CategoryId = category.Id, Title = "Kettle",
                MinPrice = 10, DesiredPrice = 20, CurrentPrice = 20, Status = ListingStatus.Sold,
                SellByDate = DateTime.UtcNow.AddDays(3)
            };
            var order = new Order
            {
                Id = Guid.NewGuid(), ListingId = listing.Id, BuyerId = buyerProfile.Id, SellerId = sellerProfile.Id,
                FinalPrice = 20, PlatformFee = 2, SellerPayout = 18, Status = status,
                DeliveryMethod = DeliveryMethod.MaltaPost, PaidAt = DateTime.UtcNow
            };
            db.UserProfiles.AddRange(buyerProfile, sellerProfile);
            db.Listings.Add(listing);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return order;
        });

        return (order, buyer, seller);
    }

    [Fact]
    public async Task Ship_ADisputedOrder_LeavesItDisputed()
    {
        // Shipping would start the auto-release clock and pay the seller during the dispute
        var (order, _, seller) = await SeedOrderAsync(OrderStatus.Disputed);
        var client = _factory.ClientFor(seller.Id);

        await MarketplaceWebFactory.PostFormAsync(client, "/Account/Settings", $"/Orders/Ship/{order.Id}",
            new() { ["TrackingNumber"] = "RR123456789MT" });

        var status = await _factory.WithDbAsync(db => db.Orders.Where(o => o.Id == order.Id).Select(o => o.Status).SingleAsync());
        Assert.Equal(OrderStatus.Disputed, status);
        Assert.False(await _factory.WithDbAsync(db => db.Shipments.AnyAsync(s => s.OrderId == order.Id)));
    }

    [Fact]
    public async Task Ship_BySomeoneOtherThanTheSeller_CreatesNoShipment()
    {
        var (order, buyer, _) = await SeedOrderAsync(OrderStatus.Paid);
        var client = _factory.ClientFor(buyer.Id);

        await MarketplaceWebFactory.PostFormAsync(client, "/Account/Settings", $"/Orders/Ship/{order.Id}",
            new() { ["TrackingNumber"] = "RR123456789MT" });

        Assert.False(await _factory.WithDbAsync(db => db.Shipments.AnyAsync(s => s.OrderId == order.Id)));
    }

    [Fact]
    public async Task Ship_APaidMaltaPostOrderBySeller_CreatesTheShipment()
    {
        var (order, _, seller) = await SeedOrderAsync(OrderStatus.Paid);
        var client = _factory.ClientFor(seller.Id);

        await MarketplaceWebFactory.PostFormAsync(client, "/Account/Settings", $"/Orders/Ship/{order.Id}",
            new() { ["TrackingNumber"] = "RR123456789MT" });

        Assert.True(await _factory.WithDbAsync(db => db.Shipments.AnyAsync(s => s.OrderId == order.Id)));
    }

    [Fact]
    public async Task Review_OfAnOrderThatIsNotCompleted_IsNotSaved()
    {
        var (order, buyer, _) = await SeedOrderAsync(OrderStatus.Paid);
        var client = _factory.ClientFor(buyer.Id);

        await MarketplaceWebFactory.PostFormAsync(client, "/Account/Settings", $"/Orders/LeaveReview/{order.Id}",
            new() { ["Rating"] = "1", ["Comment"] = "Never arrived" });

        Assert.False(await _factory.WithDbAsync(db => db.Reviews.AnyAsync(r => r.OrderId == order.Id)));
    }

    [Fact]
    public async Task Manager_CannotBanAnAdmin()
    {
        var manager = await _factory.CreateUserAsync($"manager-{Guid.NewGuid():N}@example.test", "Manager");
        var admin = await _factory.CreateUserAsync($"admin-{Guid.NewGuid():N}@example.test", "Admin");
        var client = _factory.ClientFor(manager.Id);

        await MarketplaceWebFactory.PostFormAsync(client, "/Admin/Users", "/Admin/Users?handler=Ban",
            new() { ["userId"] = admin.Id });

        var lockoutEnd = await _factory.WithDbAsync(db => db.Users.Where(u => u.Id == admin.Id).Select(u => u.LockoutEnd).SingleAsync());
        Assert.Null(lockoutEnd);
    }

    [Fact]
    public async Task Admin_CanBanARegularUser()
    {
        // The positive case, so the test above cannot pass only because the form never posts
        var admin = await _factory.CreateUserAsync($"admin-{Guid.NewGuid():N}@example.test", "Admin");
        var user = await _factory.CreateUserAsync($"user-{Guid.NewGuid():N}@example.test");
        var client = _factory.ClientFor(admin.Id);

        await MarketplaceWebFactory.PostFormAsync(client, "/Admin/Users", "/Admin/Users?handler=Ban",
            new() { ["userId"] = user.Id });

        var lockoutEnd = await _factory.WithDbAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.LockoutEnd).SingleAsync());
        Assert.NotNull(lockoutEnd);
    }

    [Fact]
    public async Task RegularUser_CannotOpenTheAdminArea()
    {
        var user = await _factory.CreateUserAsync($"user-{Guid.NewGuid():N}@example.test");

        var response = await _factory.ClientFor(user.Id).GetAsync("/Admin/Users");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DisputeUploads_AreNotServedAsStaticFiles()
    {
        var response = await _factory.CreateClient().GetAsync("/uploads/disputes/anything.jpg");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Pages_SendSecurityHeaders()
    {
        var response = await _factory.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
}
