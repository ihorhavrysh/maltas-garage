using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("UserProfiles");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(u => u.DisplayName)
            .HasMaxLength(100);

        builder.Property(u => u.AvatarUrl)
            .HasMaxLength(500);

        builder.Property(u => u.Bio)
            .HasMaxLength(500);

        builder.Property(u => u.Location)
            .HasMaxLength(100);

        builder.Property(u => u.PhoneNumber)
            .HasMaxLength(20);

        builder.Property(u => u.StripeAccountId)
            .HasMaxLength(100);

        builder.Property(u => u.Rating)
            .HasPrecision(3, 2)
            .HasDefaultValue(0);

        builder.HasIndex(u => u.UserId)
            .IsUnique();
    }
}
