using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class PriceOfferConfiguration : IEntityTypeConfiguration<PriceOffer>
{
    public void Configure(EntityTypeBuilder<PriceOffer> builder)
    {
        builder.ToTable("PriceOffers");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Amount)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(o => o.Listing)
            .WithMany()
            .HasForeignKey(o => o.ListingId)
            .OnDelete(DeleteBehavior.Restrict);

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

        builder.HasIndex(o => new { o.Status, o.ExpiresAt });
        builder.HasIndex(o => new { o.ListingId, o.BuyerId });
    }
}
