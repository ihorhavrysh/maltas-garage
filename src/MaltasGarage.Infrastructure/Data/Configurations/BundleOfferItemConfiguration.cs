using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class BundleOfferItemConfiguration : IEntityTypeConfiguration<BundleOfferItem>
{
    public void Configure(EntityTypeBuilder<BundleOfferItem> builder)
    {
        builder.ToTable("BundleOfferItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ListedPrice).HasPrecision(10, 2).IsRequired();

        builder.HasOne(i => i.BundleOffer)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.BundleOfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Listing)
            .WithMany()
            .HasForeignKey(i => i.ListingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
