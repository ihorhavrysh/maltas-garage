using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class NotificationPreferencesConfiguration : IEntityTypeConfiguration<NotificationPreferences>
{
    public void Configure(EntityTypeBuilder<NotificationPreferences> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasOne(p => p.UserProfile)
            .WithOne(u => u.NotificationPreferences)
            .HasForeignKey<NotificationPreferences>(p => p.UserProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.UserProfileId).IsUnique();
    }
}
