using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.ToTable("Listings");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.ConcurrencyStamp).IsConcurrencyToken();

        builder.Property(l => l.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(l => l.Description)
            .HasMaxLength(4000);

        builder.Property(l => l.DesiredPrice)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(l => l.MinPrice)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(l => l.CurrentPrice)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(l => l.PickupAddress)
            .HasMaxLength(500);

        builder.Property(l => l.PickupCity)
            .HasMaxLength(100);

        builder.Property(l => l.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Relationships
        builder.HasOne(l => l.Seller)
            .WithMany(u => u.Listings)
            .HasForeignKey(l => l.SellerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Category)
            .WithMany(c => c.Listings)
            .HasForeignKey(l => l.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(l => l.Status);
        builder.HasIndex(l => l.SellByDate);
        builder.HasIndex(l => l.CategoryId);
    }
}
