using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class OutboundEmailConfiguration : IEntityTypeConfiguration<OutboundEmail>
{
    public void Configure(EntityTypeBuilder<OutboundEmail> builder)
    {
        builder.HasKey(e => e.Id);

        // 320 é o limite prático de um endereço de e-mail (64 + @ + 255).
        builder.Property(e => e.ToAddress).HasMaxLength(320).IsRequired();
        builder.Property(e => e.ToName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Subject).HasMaxLength(400).IsRequired();
        builder.Property(e => e.HtmlBody).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(2000);

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.SetNull);

        // O despachante procura "pendentes cuja vez chegou".
        builder.HasIndex(e => new { e.Status, e.NextAttemptAt });
    }
}
