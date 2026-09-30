using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltasGarage.Infrastructure.Data.Configurations;

public class DisputeAttachmentConfiguration : IEntityTypeConfiguration<DisputeAttachment>
{
    public void Configure(EntityTypeBuilder<DisputeAttachment> builder)
    {
        builder.ToTable("DisputeAttachments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Url)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(a => a.FileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.HasOne(a => a.Dispute)
            .WithMany(d => d.Attachments)
            .HasForeignKey(a => a.DisputeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
