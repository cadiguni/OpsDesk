using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Sla;

public class SlaAlertOptions
{
    public const string SectionName = "SlaAlerts";

    /// <summary>Intervalo entre verificações. Zero desliga — útil em teste.</summary>
    [Range(0, 1440)]
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Quanto do prazo precisa restar para o aviso prévio sair, em porcentagem.</summary>
    [Range(1, 90)]
    public int WarningPercent { get; set; } = 25;
}

/// <summary>
/// Alertas de SLA no portal: aviso quando resta pouco do prazo e aviso quando ele vence,
/// para o prazo de resposta e para o de resolução.
///
/// Quem recebe: o responsável pelo chamado. Chamado sem responsável — ou com responsável
/// que não está mais ativo — avisa os gestores, porque do contrário o alerta que mais
/// importa, o do chamado que ninguém pegou, não iria para ninguém. O solicitante não
/// recebe: o prazo é compromisso da equipe, e avisá-lo de que a equipe está atrasada só
/// gera ansiedade sem dar a ele o que fazer.
///
/// Os prazos que não correm não alertam, pela mesma regra do indicador de vencidos:
/// resposta já dada, chamado resolvido ou cancelado, e resolução pausada enquanto o
/// chamado aguarda o solicitante (README, seção 8.2).
/// </summary>
public class SlaAlertService(
    IOpsDeskDbContext db, IBusinessCalendar calendar, IClock clock, IOptions<SlaAlertOptions> options)
{
    /// <summary>
    /// Até onde procurar prazos. O maior aviso prévio é o da resolução de prioridade baixa,
    /// 25% de 50 horas úteis; dez dias corridos cobrem isso com fim de semana e feriado no
    /// meio. A conta exata, em horas úteis, é feita depois, chamado a chamado.
    /// </summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(10);

    /// <returns>Quantos alertas saíram nesta rodada.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var horizon = now.Add(Horizon);
        var lookback = now.Subtract(SlaAlertRules.OverdueLookback);

        var candidates = await db.Tickets
            .AsNoTracking()
            .Where(t => t.Status != TicketStatus.Cancelled
                        && ((t.FirstRespondedAt == null
                             && t.SlaResponseDueAt <= horizon && t.SlaResponseDueAt >= lookback)
                            || (t.ResolvedAt == null && t.SlaPausedAt == null
                                && t.SlaResolutionDueAt <= horizon && t.SlaResolutionDueAt >= lookback)))
            .Select(t => new
            {
                t.Id,
                t.Priority,
                t.AssignedTechnicianId,
                t.FirstRespondedAt,
                t.ResolvedAt,
                t.SlaPausedAt,
                t.SlaResponseDueAt,
                t.SlaResolutionDueAt
            })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var policies = await db.SlaPolicies
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Priority, cancellationToken);

        var ids = candidates.Select(c => c.Id).ToList();

        var given = (await db.SlaAlerts
                .AsNoTracking()
                .Where(a => ids.Contains(a.TicketId))
                .Select(a => new { a.TicketId, a.Deadline, a.Stage, a.DueAt })
                .ToListAsync(cancellationToken))
            .Select(a => (a.TicketId, a.Deadline, a.Stage, a.DueAt))
            .ToHashSet();

        var activeStaff = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && (u.Role == UserRole.Technician || u.Role == UserRole.Manager))
            .Select(u => new { u.Id, u.Role })
            .ToListAsync(cancellationToken);

        var staffIds = activeStaff.Select(u => u.Id).ToHashSet();
        var managers = activeStaff.Where(u => u.Role == UserRole.Manager).Select(u => u.Id).ToList();

        var settings = options.Value;
        var sent = 0;

        foreach (var ticket in candidates)
        {
            if (!policies.TryGetValue(ticket.Priority, out var policy))
            {
                continue;
            }

            var deadlines = new List<(SlaDeadline Deadline, DateTimeOffset DueAt, int Hours)>();

            if (ticket.FirstRespondedAt is null)
            {
                deadlines.Add((SlaDeadline.Response, ticket.SlaResponseDueAt, policy.ResponseHours));
            }

            if (ticket.ResolvedAt is null && ticket.SlaPausedAt is null)
            {
                deadlines.Add((SlaDeadline.Resolution, ticket.SlaResolutionDueAt, policy.ResolutionHours));
            }

            var recipients = ticket.AssignedTechnicianId is { } assignee && staffIds.Contains(assignee)
                ? [assignee]
                : managers;

            foreach (var (deadline, dueAt, hours) in deadlines)
            {
                if (SlaAlertRules.Evaluate(dueAt, hours, settings.WarningPercent, now, calendar) is not { } stage
                    || given.Contains((ticket.Id, deadline, stage, dueAt)))
                {
                    continue;
                }

                if (await TryRecordAsync(ticket.Id, deadline, stage, dueAt, recipients, now, cancellationToken))
                {
                    given.Add((ticket.Id, deadline, stage, dueAt));
                    sent++;
                }
            }
        }

        return sent;
    }

    /// <summary>
    /// Grava o alerta e as notificações juntos. Falso quando outra rodada — de outra réplica
    /// — gravou o mesmo alerta primeiro: o índice único recusa, e nada daqui fica pendente
    /// para a próxima gravação.
    /// </summary>
    private async Task<bool> TryRecordAsync(
        Guid ticketId,
        SlaDeadline deadline,
        SlaAlertStage stage,
        DateTimeOffset dueAt,
        IReadOnlyList<Guid> recipients,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var alert = new SlaAlert { TicketId = ticketId, Deadline = deadline, Stage = stage, DueAt = dueAt, CreatedAt = now };

        var notifications = recipients
            .Select(userId => new Notification
            {
                UserId = userId,
                TicketId = ticketId,
                Kind = stage == SlaAlertStage.DueSoon ? NotificationKind.SlaDueSoon : NotificationKind.SlaOverdue,
                Detail = deadline.ToString(),
                CreatedAt = now
            })
            .ToList();

        db.SlaAlerts.Add(alert);
        db.Notifications.AddRange(notifications);

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException)
        {
            // Remover entidade ainda não gravada só a desanexa do contexto.
            db.SlaAlerts.Remove(alert);
            db.Notifications.RemoveRange(notifications);

            return false;
        }
    }
}
