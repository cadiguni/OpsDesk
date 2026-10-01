using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class EmailAccountConfiguration : IEntityTypeConfiguration<EmailAccount>
{
    public void Configure(EntityTypeBuilder<EmailAccount> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Address).HasMaxLength(320).IsRequired();
        builder.Property(a => a.DefaultPriority).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.LastError).HasMaxLength(1000);

        builder.HasIndex(a => a.Address).IsUnique();

        builder.HasOne(a => a.DefaultCategory)
            .WithMany()
            .HasForeignKey(a => a.DefaultCategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
