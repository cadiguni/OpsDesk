using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class SlaAlertConfiguration : IEntityTypeConfiguration<SlaAlert>
{
    public void Configure(EntityTypeBuilder<SlaAlert> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Deadline).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Stage).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(a => a.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.TicketId, a.Deadline, a.Stage, a.DueAt }).IsUnique();
    }
}
