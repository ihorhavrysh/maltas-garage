using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace MaltasGarage.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingImage> ListingImages => Set<ListingImage>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<HandoverCode> HandoverCodes => Set<HandoverCode>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<DisputeAttachment> DisputeAttachments => Set<DisputeAttachment>();
    public DbSet<Favourite> Favourites => Set<Favourite>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<PriceOffer> PriceOffers => Set<PriceOffer>();
    public DbSet<BundleOffer> BundleOffers => Set<BundleOffer>();
    public DbSet<BundleOfferItem> BundleOfferItems => Set<BundleOfferItem>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<SystemMessage> SystemMessages => Set<SystemMessage>();
    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();
    public DbSet<DemoReset> DemoResets => Set<DemoReset>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Apply all configurations from current assembly
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Auto-set UpdatedAt for modified entities
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is Domain.Common.BaseEntity entity)
            {
                if (entry.State == EntityState.Modified)
                {
                    entity.UpdatedAt = DateTime.UtcNow;
                }
            }

            // New optimistic concurrency stamp on every write; the original value is what
            // the UPDATE is checked against, so a concurrent writer loses cleanly
            if (entry.Entity is Domain.Common.IConcurrencyStamped stamped &&
                entry.State is EntityState.Added or EntityState.Modified)
            {
                stamped.ConcurrencyStamp = Guid.NewGuid();
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
