using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class DemoResetConfiguration : IEntityTypeConfiguration<DemoReset>
{
    public void Configure(EntityTypeBuilder<DemoReset> builder)
    {
        builder.ToTable("DemoResets");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Reason).HasMaxLength(20).IsRequired();

        builder.HasIndex(r => r.CreatedAt);
    }
}
