using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> builder)
    {
        builder.ToTable("Disputes");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Description)
            .HasMaxLength(2000);

        builder.Property(d => d.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.AdminNotes)
            .HasMaxLength(2000);

        builder.Property(d => d.Reason)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(d => d.PreviousOrderStatus)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(d => d.Resolution)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(d => d.PartialRefundAmount)
            .HasPrecision(18, 2);

        builder.HasOne(d => d.Order)
            .WithOne(o => o.Dispute)
            .HasForeignKey<Dispute>(d => d.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.OpenedBy)
            .WithMany()
            .HasForeignKey(d => d.OpenedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
