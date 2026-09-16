using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Uma política ativa por prioridade. O índice filtrado impede duas ativas
        // simultâneas, que fariam o prazo depender da ordem da query.
        builder.HasIndex(p => p.Priority)
            .IsUnique()
            .HasFilter("is_active");
    }
}
