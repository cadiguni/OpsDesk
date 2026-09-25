using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence;

public class OpsDeskDbContext(DbContextOptions<OpsDeskDbContext> options)
    : DbContext(options), IOpsDeskDbContext
{
    /// <summary>Sequence que gera <c>Ticket.Code</c>. Ver invariante 5 do CLAUDE.md.</summary>
    public const string TicketCodeSequence = "ticket_code_seq";

    public DbSet<User> Users => Set<User>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<TicketComment> TicketComments => Set<TicketComment>();

    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();

    public DbSet<TicketHistory> TicketHistory => Set<TicketHistory>();

    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();

    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();

    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();

    public DbSet<SettingsAuditEntry> SettingsAudit => Set<SettingsAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(TicketCodeSequence).StartsAt(1).IncrementsBy(1);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OpsDeskDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Invariante 4 do CLAUDE.md: toda data é timestamptz. DateTimeOffset é o tipo
        // padrão do domínio justamente porque o Npgsql rejeita DateTime Unspecified aqui.
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamptz");
        configurationBuilder.Properties<DateTimeOffset?>().HaveColumnType("timestamptz");
    }
}
