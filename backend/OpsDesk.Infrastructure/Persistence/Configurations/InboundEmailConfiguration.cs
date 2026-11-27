using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class InboundEmailConfiguration : IEntityTypeConfiguration<InboundEmail>
{
    public void Configure(EntityTypeBuilder<InboundEmail> builder)
    {
        builder.HasKey(e => e.Id);

        // 998 é o limite de linha de cabeçalho da RFC 5322.
        builder.Property(e => e.MessageId).HasMaxLength(998);
        builder.Property(e => e.ProviderId).HasMaxLength(512);
        builder.Property(e => e.InReplyTo).HasMaxLength(998);
        builder.Property(e => e.References).HasMaxLength(8000);
        builder.Property(e => e.FromName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.FromAddress).HasMaxLength(320).IsRequired();
        builder.Property(e => e.ToAddress).HasMaxLength(320);
        builder.Property(e => e.Subject).HasMaxLength(998).IsRequired();
        builder.Property(e => e.BodyText).IsRequired();
        builder.Property(e => e.HeadersJson);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.SpamReason).HasMaxLength(500);
        builder.Property(e => e.ProcessingError).HasMaxLength(2000);

        builder.HasOne(e => e.EmailAccount)
            .WithMany()
            .HasForeignKey(e => e.EmailAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Ticket)
            .WithMany()
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.ReviewedById)
            .OnDelete(DeleteBehavior.SetNull);

        // Reentrega: a busca por Message-ID na mesma caixa.
        builder.HasIndex(e => new { e.EmailAccountId, e.MessageId });

        // A tela filtra por situação e ordena por chegada; a fila de nova tentativa também.
        builder.HasIndex(e => new { e.Status, e.ReceivedAt });
    }
}
