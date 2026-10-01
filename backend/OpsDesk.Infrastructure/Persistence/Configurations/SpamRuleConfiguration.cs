using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class SpamRuleConfiguration : IEntityTypeConfiguration<SpamRule>
{
    public void Configure(EntityTypeBuilder<SpamRule> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.Action).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(r => r.Pattern).HasMaxLength(320).IsRequired();

        builder.HasOne(r => r.CreatedBy)
            .WithMany()
            .HasForeignKey(r => r.CreatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
