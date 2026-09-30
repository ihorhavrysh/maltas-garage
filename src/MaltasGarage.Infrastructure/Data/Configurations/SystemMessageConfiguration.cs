using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class SystemMessageConfiguration : IEntityTypeConfiguration<SystemMessage>
{
    public void Configure(EntityTypeBuilder<SystemMessage> builder)
    {
        builder.ToTable("SystemMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Body)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(m => m.Link)
            .HasMaxLength(200);

        builder.Property(m => m.Type)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.UserId, m.IsRead });
    }
}
