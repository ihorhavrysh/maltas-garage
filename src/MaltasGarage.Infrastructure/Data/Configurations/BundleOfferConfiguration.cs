using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class BundleOfferConfiguration : IEntityTypeConfiguration<BundleOffer>
{
    public void Configure(EntityTypeBuilder<BundleOffer> builder)
    {
        builder.ToTable("BundleOffers");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OfferAmount).HasPrecision(10, 2).IsRequired();
        builder.Property(o => o.TotalListedPrice).HasPrecision(10, 2).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(o => o.Buyer)
            .WithMany()
            .HasForeignKey(o => o.BuyerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Seller)
            .WithMany()
            .HasForeignKey(o => o.SellerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Conversation)
            .WithMany()
            .HasForeignKey(o => o.ConversationId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(o => new { o.BuyerId, o.SellerId });
        builder.HasIndex(o => new { o.Status, o.ExpiresAt });
    }
}
