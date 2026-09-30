using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<Listing> Listings { get; }
    DbSet<ListingImage> ListingImages { get; }
    DbSet<Bid> Bids { get; }
    DbSet<Order> Orders { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Shipment> Shipments { get; }
    DbSet<HandoverCode> HandoverCodes { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Dispute> Disputes { get; }
    DbSet<DisputeAttachment> DisputeAttachments { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<SystemMessage> SystemMessages { get; }
    DbSet<NotificationPreferences> NotificationPreferences { get; }
    DbSet<PriceOffer> PriceOffers { get; }
    DbSet<BundleOffer> BundleOffers { get; }
    DbSet<BundleOfferItem> BundleOfferItems { get; }
    DbSet<OrderItem> OrderItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
