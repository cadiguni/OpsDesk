using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.HasKey(t => t.Id);

        // Invariante 5 do CLAUDE.md: o código vem da sequence, formatado pelo próprio
        // banco e lido de volta na mesma transação da inserção (RETURNING). Assim dois
        // chamados criados no mesmo instante não podem receber o mesmo código.
        builder.Property(t => t.Code)
            .HasMaxLength(20)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql(
                $"'OPS-' || lpad(nextval('{OpsDeskDbContext.TicketCodeSequence}')::text, 6, '0')");

        builder.HasIndex(t => t.Code).IsUnique();

        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).IsRequired();

        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(t => t.ExternalId).HasMaxLength(500);

        builder.HasOne(t => t.Requester)
            .WithMany(u => u.RequestedTickets)
            .HasForeignKey(t => t.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.AssignedTechnician)
            .WithMany(u => u.AssignedTickets)
            .HasForeignKey(t => t.AssignedTechnicianId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Category)
            .WithMany(c => c.Tickets)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índices desenhados para as três telas de listagem: "meus chamados" filtra por
        // solicitante, o painel do técnico filtra por responsável e por status, e o
        // dashboard agrega por status e prioridade.
        builder.HasIndex(t => new { t.RequesterId, t.Status });
        builder.HasIndex(t => new { t.AssignedTechnicianId, t.Status });
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.CreatedAt);

        // Ordenação por última atualização, na listagem.
        builder.HasIndex(t => t.UpdatedAt);

        // Vencidos são consultados pelos dois prazos; sem isso o painel varre a tabela.
        builder.HasIndex(t => t.SlaResolutionDueAt);
        builder.HasIndex(t => t.SlaResponseDueAt);

        // Origem externa é única quando existe: evita que o mesmo e-mail reprocessado
        // abra dois chamados (versão 2.0, docs/integracao-email.md).
        builder.HasIndex(t => new { t.Source, t.ExternalId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");

        builder.Ignore(t => t.IsClosedOut);
        builder.Ignore(t => t.CountsForSla);
    }
}
