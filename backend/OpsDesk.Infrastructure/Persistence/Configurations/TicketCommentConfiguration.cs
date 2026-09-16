using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Content).IsRequired();
        builder.Property(c => c.IsInternal).HasDefaultValue(false);

        builder.HasOne(c => c.Ticket)
            .WithMany(t => t.Comments)
            .HasForeignKey(c => c.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Author)
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        // A tela de detalhe lê comentários de um chamado em ordem cronológica, e o
        // filtro de visibilidade corta por IsInternal na mesma query.
        builder.HasIndex(c => new { c.TicketId, c.IsInternal, c.CreatedAt });
    }
}
