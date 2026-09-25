using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class EmailSettingsConfiguration : IEntityTypeConfiguration<EmailSettings>
{
    public void Configure(EntityTypeBuilder<EmailSettings> builder)
    {
        builder.HasKey(s => s.Id);

        // Linha única: o id é sempre 1, nunca gerado.
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.SenderAddress).HasMaxLength(320);
        builder.Property(s => s.SenderName).HasMaxLength(200);
        builder.Property(s => s.TenantId).HasMaxLength(100);
        builder.Property(s => s.ClientId).HasMaxLength(100);
        builder.Property(s => s.ProtectedClientSecret).HasMaxLength(4000);
        builder.Property(s => s.PortalUrl).HasMaxLength(500);
        builder.Property(s => s.LastError).HasMaxLength(2000);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UpdatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
