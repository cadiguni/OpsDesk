using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class UserAuditEntryConfiguration : IEntityTypeConfiguration<UserAuditEntry>
{
    public void Configure(EntityTypeBuilder<UserAuditEntry> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(a => a.PreviousValue).HasMaxLength(200);
        builder.Property(a => a.NewValue).HasMaxLength(200);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Quem fez a mudança nunca é apagado junto: a trilha perderia o sentido.
        builder.HasOne(a => a.ChangedBy)
            .WithMany()
            .HasForeignKey(a => a.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.UserId, a.CreatedAt });
    }
}
