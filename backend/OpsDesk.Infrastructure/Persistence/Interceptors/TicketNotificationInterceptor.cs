using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Notifications;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Gera notificações e e-mails a partir do que mudou nos chamados, na mesma gravação.
///
/// Pelo mesmo motivo do <see cref="TicketHistoryInterceptor"/>: aviso que depende de cada
/// serviço lembrar de chamar alguma coisa funciona até o primeiro endpoint novo. Aqui o
/// gatilho é o dado — comentário novo, status ou responsável diferente —, e qualquer
/// caminho que grave isso notifica.
///
/// Este interceptor só coleta. A regra de quem recebe o quê mora no
/// <see cref="NotificationPlanner"/>, que é puro e testado à parte.
///
/// Estar na mesma transação é o que dá a garantia: se a mudança for gravada, o aviso
/// também é; se ela falhar, não sai aviso de algo que não aconteceu.
/// </summary>
public class TicketNotificationInterceptor(ICurrentUser currentUser, IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        // Nenhum caminho da aplicação grava de forma síncrona; se algum passar a gravar, o
        // aviso não pode sumir por isso.
        PlanAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await PlanAsync(eventData.Context, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task PlanAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return;
        }

        var changes = Collect(context);

        if (changes.Count == 0)
        {
            return;
        }

        var people = await LoadPeopleAsync(context, changes, cancellationToken);
        var channel = await EmailChannelAsync(context, cancellationToken);
        var actorId = currentUser.UserId;
        var now = clock.UtcNow;

        foreach (var raw in changes.Values)
        {
            if (!people.TryGetValue(raw.RequesterId, out var requester))
            {
                continue;
            }

            var change = new TicketChange(
                raw.TicketId,
                raw.Code,
                raw.Title,
                requester,
                raw.CurrentAssigneeId is { } current ? people.GetValueOrDefault(current) : null,
                raw.AssigneeChanged,
                raw.PreviousAssigneeId is { } previous ? people.GetValueOrDefault(previous) : null,
                raw.CurrentStatus,
                raw.PreviousStatus,
                raw.Comments
                    .Select(c => new AddedComment(
                        c.AuthorId,
                        people.GetValueOrDefault(c.AuthorId)?.Name ?? "Equipe",
                        c.Content,
                        c.IsInternal))
                    .ToList());

            var planned = NotificationPlanner.Plan(change, actorId, now, channel);

            context.Set<Notification>().AddRange(planned.InApp);
            context.Set<OutboundEmail>().AddRange(planned.Emails);
        }
    }

    private sealed class RawChange
    {
        public Guid TicketId { get; init; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public Guid RequesterId { get; set; }
        public Guid? CurrentAssigneeId { get; set; }
        public bool AssigneeChanged { get; set; }
        public Guid? PreviousAssigneeId { get; set; }
        public TicketStatus CurrentStatus { get; set; }
        public TicketStatus? PreviousStatus { get; set; }
        public List<TicketComment> Comments { get; } = [];
        public bool Loaded { get; set; }
    }

    /// <summary>
    /// Chamados alterados e comentários novos, agrupados por chamado. Chamado recém-criado
    /// fica de fora: abertura não é um dos eventos que avisam (README, seção 18).
    /// </summary>
    private static Dictionary<Guid, RawChange> Collect(DbContext context)
    {
        var changes = new Dictionary<Guid, RawChange>();

        foreach (var entry in context.ChangeTracker.Entries<Ticket>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var ticket = entry.Entity;
            var status = entry.Property(t => t.Status);
            var assignee = entry.Property(t => t.AssignedTechnicianId);

            var statusChanged = status.IsModified && status.OriginalValue != status.CurrentValue;
            var assigneeChanged = assignee.IsModified && assignee.OriginalValue != assignee.CurrentValue;

            var change = For(changes, ticket.Id);
            Fill(change, ticket);

            if (statusChanged)
            {
                change.PreviousStatus = status.OriginalValue;
            }

            if (assigneeChanged)
            {
                change.AssigneeChanged = true;
                change.PreviousAssigneeId = assignee.OriginalValue;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<TicketComment>())
        {
            if (entry.State == EntityState.Added)
            {
                For(changes, entry.Entity.TicketId).Comments.Add(entry.Entity);
            }
        }

        // Chamado só tocado por UpdatedAt, sem comentário, não é evento.
        foreach (var (id, change) in changes.ToList())
        {
            if (change.Comments.Count == 0 && change.PreviousStatus is null && !change.AssigneeChanged)
            {
                changes.Remove(id);
            }
        }

        return changes;
    }

    private static RawChange For(Dictionary<Guid, RawChange> changes, Guid ticketId)
    {
        if (!changes.TryGetValue(ticketId, out var change))
        {
            change = new RawChange { TicketId = ticketId };
            changes[ticketId] = change;
        }

        return change;
    }

    private static void Fill(RawChange change, Ticket ticket)
    {
        change.Code = ticket.Code;
        change.Title = ticket.Title;
        change.RequesterId = ticket.RequesterId;
        change.CurrentAssigneeId = ticket.AssignedTechnicianId;
        change.CurrentStatus = ticket.Status;
        change.Loaded = true;
    }

    /// <summary>
    /// Completa chamado que só apareceu por comentário, e carrega todas as pessoas
    /// envolvidas numa consulta só.
    /// </summary>
    private static async Task<Dictionary<Guid, NotificationPerson>> LoadPeopleAsync(
        DbContext context, Dictionary<Guid, RawChange> changes, CancellationToken cancellationToken)
    {
        var missing = changes.Values.Where(c => !c.Loaded).Select(c => c.TicketId).ToList();

        if (missing.Count > 0)
        {
            var tickets = await context.Set<Ticket>()
                .AsNoTracking()
                .Where(t => missing.Contains(t.Id))
                .ToListAsync(cancellationToken);

            foreach (var ticket in tickets)
            {
                Fill(changes[ticket.Id], ticket);
            }
        }

        var ids = changes.Values
            .SelectMany(c => new[] { c.RequesterId, c.CurrentAssigneeId, c.PreviousAssigneeId }
                .Concat(c.Comments.Select(comment => (Guid?)comment.AuthorId)))
            .OfType<Guid>()
            .Distinct()
            .ToList();

        return await context.Set<User>()
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(
                u => u.Id,
                u => new NotificationPerson(u.Id, u.Name, u.Email, u.Role, u.IsActive),
                cancellationToken);
    }

    /// <summary>
    /// E-mail só entra na fila com o envio ligado. Desligado, nada se acumula para sair de
    /// uma vez quando alguém ligar.
    /// </summary>
    private static async Task<EmailChannel?> EmailChannelAsync(DbContext context, CancellationToken cancellationToken)
    {
        var settings = await context.Set<EmailSettings>()
            .AsNoTracking()
            .Select(s => new { s.IsEnabled, s.PortalUrl })
            .SingleOrDefaultAsync(cancellationToken);

        return settings is { IsEnabled: true } ? new EmailChannel(settings.PortalUrl) : null;
    }
}
