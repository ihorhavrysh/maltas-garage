using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.StripePaymentIntentId)
            .HasMaxLength(100);

        // Payments are looked up by PaymentIntent (webhook, return pages), and one PaymentIntent
        // pays for one order: the unique index makes a second record for it impossible
        builder.HasIndex(p => p.StripePaymentIntentId)
            .IsUnique()
            .HasFilter("[StripePaymentIntentId] IS NOT NULL");

        builder.Property(p => p.StripeTransferId)
            .HasMaxLength(100);

        builder.Property(p => p.Amount)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(p => p.Order)
            .WithOne(o => o.Payment)
            .HasForeignKey<Payment>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
