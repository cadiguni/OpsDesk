using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class SettingsAuditEntryConfiguration : IEntityTypeConfiguration<SettingsAuditEntry>
{
    public void Configure(EntityTypeBuilder<SettingsAuditEntry> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Area).HasMaxLength(40).IsRequired();
        builder.Property(a => a.ChangedFields).HasMaxLength(500).IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.Area, a.CreatedAt });
    }
}
