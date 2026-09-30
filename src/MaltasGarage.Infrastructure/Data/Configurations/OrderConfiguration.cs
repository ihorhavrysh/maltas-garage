using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.ConcurrencyStamp).IsConcurrencyToken();

        builder.Property(o => o.FinalPrice)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(o => o.PlatformFee)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(o => o.SellerPayout)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.DeliveryMethod)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.DeliveryMethodPending)
            .HasDefaultValue(false);

        builder.Property(o => o.IsBundleOrder).HasDefaultValue(false);

        // Relationships
        builder.HasOne(o => o.Listing)
            .WithOne(l => l.Order)
            .HasForeignKey<Order>(o => o.ListingId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Buyer)
            .WithMany(u => u.BuyerOrders)
            .HasForeignKey(o => o.BuyerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Seller)
            .WithMany(u => u.SellerOrders)
            .HasForeignKey(o => o.SellerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.BuyerId);
        builder.HasIndex(o => o.SellerId);
    }
}
