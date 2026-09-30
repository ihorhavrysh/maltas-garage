using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class HandoverCodeConfiguration : IEntityTypeConfiguration<HandoverCode>
{
    public void Configure(EntityTypeBuilder<HandoverCode> builder)
    {
        builder.ToTable("HandoverCodes");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Code)
            .HasMaxLength(6)
            .IsRequired();

        builder.Property(h => h.QrCodeUrl)
            .HasMaxLength(500);

        builder.HasOne(h => h.Order)
            .WithOne(o => o.HandoverCode)
            .HasForeignKey<HandoverCode>(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(h => h.Code);
    }
}
