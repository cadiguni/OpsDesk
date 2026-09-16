using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Date).HasColumnType("date").IsRequired();
        builder.Property(h => h.Name).HasMaxLength(200).IsRequired();

        builder.HasIndex(h => h.Date).IsUnique();
    }
}
