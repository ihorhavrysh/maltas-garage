using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class UserProfile : BaseEntity
{
    public string UserId { get; set; } = string.Empty; // ASP.NET Identity User Id
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string? Location { get; set; }
    public string? PhoneNumber { get; set; }

    // Stripe Connect
    public string? StripeAccountId { get; set; }
    public bool StripeOnboardingComplete { get; set; }

    // Stats
    public decimal Rating { get; set; }
    public int TotalReviews { get; set; }
    public int TotalSales { get; set; }

    // Demo: the public demo accounts (credentials in the README). Their password, email and
    // profile cannot be changed and they cannot be deleted or banned.
    public bool IsDemoAccount { get; set; }

    // Navigation properties
    public NotificationPreferences? NotificationPreferences { get; set; }
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
    public ICollection<Order> BuyerOrders { get; set; } = new List<Order>();
    public ICollection<Order> SellerOrders { get; set; } = new List<Order>();
    public ICollection<Review> ReviewsReceived { get; set; } = new List<Review>();
    public ICollection<Review> ReviewsGiven { get; set; } = new List<Review>();
}
