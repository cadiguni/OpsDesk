using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class SystemSettingsConfiguration : IEntityTypeConfiguration<SystemSettings>
{
    public void Configure(EntityTypeBuilder<SystemSettings> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.OrganizationName).HasMaxLength(100);
        builder.Property(s => s.AllowedEmailDomains).HasMaxLength(500);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UpdatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
