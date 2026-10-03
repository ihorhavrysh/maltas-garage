using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");

        builder.HasKey(c => c.Id);

        builder.HasOne(c => c.Buyer)
            .WithMany()
            .HasForeignKey(c => c.BuyerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Seller)
            .WithMany()
            .HasForeignKey(c => c.SellerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.BuyerId, c.SellerId })
            .IsUnique();
    }
}
