using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class TicketHistoryConfiguration : IEntityTypeConfiguration<TicketHistory>
{
    public void Configure(EntityTypeBuilder<TicketHistory> builder)
    {
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Action).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(h => h.PreviousValue).HasMaxLength(500);
        builder.Property(h => h.NewValue).HasMaxLength(500);

        builder.HasOne(h => h.Ticket)
            .WithMany(t => t.History)
            .HasForeignKey(h => h.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // O autor da ação nunca é apagado junto: o histórico perderia sentido.
        builder.HasOne(h => h.ChangedBy)
            .WithMany()
            .HasForeignKey(h => h.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.TicketId, h.CreatedAt });
    }
}
