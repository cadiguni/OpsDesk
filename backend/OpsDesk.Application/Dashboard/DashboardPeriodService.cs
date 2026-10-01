using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Domain.Tickets;

namespace OpsDesk.Application.Dashboard;

/// <summary>
/// Indicadores de um período. Os percentuais são nulos quando não há o que medir — "0% de
/// cumprimento" e "nenhum prazo decidido" são coisas diferentes, e a tela precisa mostrar
/// a diferença.
/// </summary>
public record PeriodIndicators(
    int Opened,
    int Resolved,
    int ResponseMet,
    int ResponseMissed,
    double? ResponseCompliance,
    int ResolutionMet,
    int ResolutionMissed,
    double? ResolutionCompliance,
    int Reopened,
    double? ReopenRate);

public record TechnicianPerformance(Guid Id, string Name, int Resolved, double? ResolutionCompliance, int Reopened);

public record DashboardPeriod(
    DateTimeOffset From,
    DateTimeOffset Before,
    PeriodIndicators Current,
    PeriodIndicators Previous,
    IReadOnlyList<TechnicianPerformance> ByTechnician);

public abstract record DashboardPeriodResult
{
    public sealed record Ok(DashboardPeriod Period) : DashboardPeriodResult;

    public sealed record Invalid(string Message) : DashboardPeriodResult;
}

/// <summary>
/// "Como fomos no período?" (README, seção 13.7): volume, cumprimento de SLA e reabertura,
/// com o período anterior de mesmo tamanho para comparação.
///
/// Complementa o <see cref="DashboardService"/>, que é a foto da fila agora. Tudo por
/// consulta agregada, como lá.
///
/// Os critérios:
///
/// <list type="bullet">
/// <item><b>resposta</b> — chamados abertos no período: cumpriu quem respondeu até o prazo;
/// não cumpriu quem respondeu depois, ou ainda não respondeu e o prazo passou. Prazo ainda
/// correndo fica de fora: não conta a favor nem contra;</item>
/// <item><b>resolução</b> — chamados resolvidos no período: cumpriu quem resolveu até o
/// prazo, já descontadas as pausas;</item>
/// <item><b>reabertura</b> — reaberturas registradas no histórico durante o período,
/// sobre as resoluções do período. É o indicador de solução que não resolveu.</item>
/// </list>
///
/// Cancelado fica fora de tudo, como em todo indicador de SLA (README, seção 8.3).
/// </summary>
public class DashboardPeriodService(IOpsDeskDbContext db, IClock clock)
{
    /// <summary>Um ano e pouco: cobre "este ano" e a comparação, sem consulta sem fim.</summary>
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);

    public async Task<DashboardPeriodResult> GetAsync(
        DateTimeOffset from, DateTimeOffset before, TicketViewer viewer, CancellationToken cancellationToken = default)
    {
        // Em UTC antes da consulta: a interface manda o deslocamento de São Paulo, e o
        // Npgsql recusa parâmetro timestamptz com deslocamento diferente de zero.
        from = from.ToUniversalTime();
        before = before.ToUniversalTime();

        if (before <= from)
        {
            return new DashboardPeriodResult.Invalid("O fim do período precisa ser depois do início.");
        }

        if (before - from > MaxRange)
        {
            return new DashboardPeriodResult.Invalid("O período pode ter no máximo um ano.");
        }

        var length = before - from;
        var now = clock.UtcNow;
        var tickets = db.Tickets.AsNoTracking().VisibleTo(viewer)
            .Where(t => t.Status != TicketStatus.Cancelled && TicketSla.MeasuredSources.Contains(t.Source));

        var current = await IndicatorsAsync(tickets, from, before, now, cancellationToken);
        var previous = await IndicatorsAsync(tickets, from - length, from, now, cancellationToken);
        var byTechnician = await ByTechnicianAsync(tickets, from, before, cancellationToken);

        return new DashboardPeriodResult.Ok(new DashboardPeriod(from, before, current, previous, byTechnician));
    }

    private async Task<PeriodIndicators> IndicatorsAsync(
        IQueryable<Ticket> tickets, DateTimeOffset from, DateTimeOffset before, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var opened = tickets.Where(t => t.CreatedAt >= from && t.CreatedAt < before);
        var resolved = tickets.Where(t => t.ResolvedAt >= from && t.ResolvedAt < before);

        var openedCount = await opened.CountAsync(cancellationToken);

        var responseMet = await opened.CountAsync(
            t => t.FirstRespondedAt != null && t.FirstRespondedAt <= t.SlaResponseDueAt, cancellationToken);

        var responseMissed = await opened.CountAsync(
            t => (t.FirstRespondedAt != null && t.FirstRespondedAt > t.SlaResponseDueAt)
                 || (t.FirstRespondedAt == null && t.SlaResponseDueAt < now),
            cancellationToken);

        var resolvedCount = await resolved.CountAsync(cancellationToken);
        var resolutionMet = await resolved.CountAsync(t => t.ResolvedAt <= t.SlaResolutionDueAt, cancellationToken);

        var reopened = await ReopeningsIn(tickets, from, before).CountAsync(cancellationToken);

        return new PeriodIndicators(
            openedCount,
            resolvedCount,
            responseMet,
            responseMissed,
            Rate(responseMet, responseMet + responseMissed),
            resolutionMet,
            resolvedCount - resolutionMet,
            Rate(resolutionMet, resolvedCount),
            reopened,
            Rate(reopened, resolvedCount));
    }

    private async Task<List<TechnicianPerformance>> ByTechnicianAsync(
        IQueryable<Ticket> tickets, DateTimeOffset from, DateTimeOffset before, CancellationToken cancellationToken)
    {
        var resolved = await tickets
            .Where(t => t.ResolvedAt >= from && t.ResolvedAt < before && t.AssignedTechnicianId != null)
            .GroupBy(t => new { Id = t.AssignedTechnicianId!.Value, t.AssignedTechnician!.Name })
            .Select(g => new
            {
                g.Key.Id,
                g.Key.Name,
                Resolved = g.Count(),
                Met = g.Count(t => t.ResolvedAt <= t.SlaResolutionDueAt)
            })
            .ToListAsync(cancellationToken);

        // A reabertura conta para quem está com o chamado agora — em geral, quem o tinha
        // resolvido. Se ele foi repassado depois, o indicador segue o chamado.
        var reopened = await ReopeningsIn(tickets, from, before)
            .Where(h => h.Ticket.AssignedTechnicianId != null)
            .GroupBy(h => h.Ticket.AssignedTechnicianId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return resolved
            .Select(r => new TechnicianPerformance(
                r.Id, r.Name, r.Resolved, Rate(r.Met, r.Resolved), reopened.GetValueOrDefault(r.Id)))
            .OrderByDescending(r => r.Resolved)
            .ThenBy(r => r.Name)
            .ToList();
    }

    private IQueryable<TicketHistory> ReopeningsIn(IQueryable<Ticket> tickets, DateTimeOffset from, DateTimeOffset before) =>
        db.TicketHistory.Where(h =>
            h.Action == TicketHistoryAction.Reopened
            && h.CreatedAt >= from && h.CreatedAt < before
            && tickets.Any(t => t.Id == h.TicketId));

    private static double? Rate(int part, int whole) =>
        whole == 0 ? null : Math.Round(100.0 * part / whole, 1);
}
