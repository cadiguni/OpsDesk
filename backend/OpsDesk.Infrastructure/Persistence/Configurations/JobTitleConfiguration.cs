using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class JobTitleConfiguration : IEntityTypeConfiguration<JobTitle>
{
    public void Configure(EntityTypeBuilder<JobTitle> builder)
    {
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(j => j.Name).IsUnique();

        // Cargo não se apaga, só se desativa; Restrict é a rede caso alguém tente.
        builder.HasMany(j => j.Users)
            .WithOne(u => u.JobTitle)
            .HasForeignKey(u => u.JobTitleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
