using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class TicketAttachmentConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).IsRequired().HasMaxLength(260);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(180);
        builder.Property(a => a.StorageKey).IsRequired().HasMaxLength(200);
        builder.Property(a => a.IsInternal).HasDefaultValue(false);

        // Apagar o chamado leva os anexos junto; o arquivo em disco é removido pelo
        // serviço, que é quem conhece o armazenamento.
        builder.HasOne(a => a.Ticket)
            .WithMany(t => t.Attachments)
            .HasForeignKey(a => a.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Comment)
            .WithMany(c => c.Attachments)
            .HasForeignKey(a => a.CommentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.UploadedBy)
            .WithMany()
            .HasForeignKey(a => a.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);

        // A leitura é sempre "anexos deste chamado", e o filtro de visibilidade corta por
        // IsInternal na mesma query — mesmo desenho do índice de comentários.
        builder.HasIndex(a => new { a.TicketId, a.IsInternal, a.CreatedAt });

        // A varredura de pendentes órfãos procura por quem enviou e quando.
        builder.HasIndex(a => new { a.UploadedById, a.CreatedAt })
            .HasFilter("ticket_id IS NULL");
    }
}
