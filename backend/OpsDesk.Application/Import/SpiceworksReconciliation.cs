using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Sla;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Import;

/// <summary>Um chamado do OpsDesk que pede atenção à parte na conciliação.</summary>
public record ReconciliationNote(int SpiceworksNumber, string? Code, string Message);

/// <summary>O que a conciliação encontrou (e, fora da conferência, aplicou).</summary>
public record SpiceworksReconciliationReport(
    bool DryRun,
    int Tickets,
    int WithoutMessageId,
    int Matched,
    int NotFound,
    int DatesFixed,
    int Closed,
    int StillOpen,
    IReadOnlyList<ReconciliationNote> Notes);

/// <summary>
/// Concilia os chamados que o OpsDesk abriu a partir da caixa de e-mail com a exportação
/// JSON do Spiceworks Cloud Help Desk, a ferramenta que atendia esses mesmos chamados.
///
/// A caixa monitorada trouxe mensagens antigas, e o chamado aberto de cada uma nasceu com a
/// data do processamento, aberto e com prazo de SLA contado dali. O Spiceworks guarda a data
/// real e se o chamado já foi fechado. O elo entre os dois é o Message-ID do e-mail de
/// abertura: o <c>email_message_id</c> lá, o <c>ExternalId</c> aqui.
///
/// Para cada chamado casado:
///
/// <list type="bullet">
/// <item><b>datas</b> — abertura, primeira resposta e última atualização passam a ser as do
/// Spiceworks, e os prazos de SLA são recalculados a partir da abertura real;</item>
/// <item><b>fechado lá, fechado aqui</b> — chamado em andamento no OpsDesk e fechado no
/// Spiceworks fica fechado, com a data de fechamento de lá. Já resolvido ou fechado aqui só
/// recebe a data; cancelado não muda;</item>
/// <item><b>aberto lá</b> — continua como está aqui. Se aqui já terminou, a nota avisa.</item>
/// </list>
///
/// A gravação é por <c>ExecuteUpdate</c>, que passa ao largo dos interceptores, de
/// propósito: isto é correção de origem, não atendimento. Fechar pelo caminho normal mandaria
/// um aviso a cada solicitante de chamados atendidos há meses, e o histórico registraria como
/// ação de hoje o que aconteceu em outra ferramenta. Pelo mesmo motivo, aplicar de novo não
/// muda nada: a segunda rodada encontra tudo já conciliado.
/// </summary>
public class SpiceworksReconciliation(
    IOpsDeskDbContext db,
    IBusinessCalendar calendar,
    SlaClock sla,
    ILogger<SpiceworksReconciliation> logger)
{
    /// <summary>
    /// Abaixo dos 30 MB do Kestrel e dos 32 MB do nginx do SPA: acima disso o arquivo seria
    /// recusado antes de chegar aqui, sem a mensagem desta tela.
    /// </summary>
    public const long MaxFileBytes = 25 * 1024 * 1024;

    private const int MaxNotes = 200;

    private sealed record SpiceworksTicket(
        int Number,
        string MessageId,
        bool IsClosed,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ClosedAt,
        DateTimeOffset? UpdatedAt,
        long? FirstResponseSeconds);

    public async Task<SpiceworksReconciliationReport> RunAsync(
        string content, bool dryRun, CancellationToken cancellationToken = default)
    {
        var (total, exported) = Parse(content);
        var notes = new List<ReconciliationNote>();

        var tickets = (await db.Tickets.AsNoTracking()
                .Where(t => t.Source == TicketSource.Email && t.ExternalId != null)
                .Select(t => new
                {
                    t.Id,
                    t.Code,
                    t.ExternalId,
                    t.Status,
                    t.Priority,
                    t.CreatedAt,
                    t.FirstRespondedAt,
                    t.ResolvedAt,
                    t.ClosedAt,
                    t.SlaResponseDueAt,
                    t.SlaResolutionDueAt,
                    t.SlaPausedAt,
                    t.SlaPausedBusinessMinutes
                })
                .ToListAsync(cancellationToken))
            .GroupBy(t => Key(t.ExternalId!))
            .ToDictionary(g => g.Key, g => g.First());

        var policies = await db.SlaPolicies.AsNoTracking()
            .Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Priority, cancellationToken);

        int matched = 0, notFound = 0, datesFixed = 0, closed = 0, stillOpen = 0;

        foreach (var sw in exported)
        {
            if (!tickets.TryGetValue(Key(sw.MessageId), out var ticket))
            {
                notFound++;

                if (!sw.IsClosed)
                {
                    Note(notes, sw.Number, null, "aberto no Spiceworks e sem chamado correspondente no OpsDesk.");
                }

                continue;
            }

            // Duas linhas do Spiceworks com a mesma mensagem (chamado mesclado): vale a primeira.
            tickets.Remove(Key(sw.MessageId));
            matched++;

            var finishedHere = ticket.Status is TicketStatus.Resolved or TicketStatus.Closed or TicketStatus.Cancelled;
            var close = sw.IsClosed && !finishedHere;
            var finishedAt = sw.ClosedAt ?? sw.UpdatedAt ?? sw.CreatedAt;

            if (!sw.IsClosed)
            {
                stillOpen++;

                if (finishedHere)
                {
                    Note(notes, sw.Number, ticket.Code, "aberto no Spiceworks, mas já terminado no OpsDesk. Ficou como está.");
                }
            }

            if (close)
            {
                closed++;
            }

            var datesDiffer = Math.Abs((ticket.CreatedAt - sw.CreatedAt).TotalMinutes) >= 1;

            if (datesDiffer)
            {
                datesFixed++;
            }

            // Fechado aqui antes da conciliação (pela ação em lote, por exemplo): só a data muda.
            var redate = sw.IsClosed
                         && (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
                         && (ticket.ResolvedAt != finishedAt || (ticket.Status == TicketStatus.Closed && ticket.ClosedAt != finishedAt));

            if (dryRun || (!datesDiffer && !close && !redate))
            {
                continue;
            }

            var responseDue = ticket.SlaResponseDueAt;
            var resolutionDue = ticket.SlaResolutionDueAt;

            if (policies.TryGetValue(ticket.Priority, out var policy))
            {
                var deadlines = sla.Calculate(sw.CreatedAt, policy);
                responseDue = deadlines.ResponseDueAt;
                resolutionDue = ticket.SlaPausedBusinessMinutes > 0
                    ? calendar.AddBusinessMinutes(deadlines.ResolutionDueAt, ticket.SlaPausedBusinessMinutes)
                    : deadlines.ResolutionDueAt;
            }

            var firstResponded = sw.FirstResponseSeconds is > 0 and var seconds
                ? sw.CreatedAt.AddSeconds(seconds)
                : ticket.FirstRespondedAt;
            var updatedAt = sw.UpdatedAt is { } u && u > sw.CreatedAt ? u : sw.CreatedAt;

            var status = close ? TicketStatus.Closed : ticket.Status;
            var resolvedAt = close || redate ? finishedAt : ticket.ResolvedAt;
            var closedAt = close || (redate && ticket.Status == TicketStatus.Closed) ? finishedAt : ticket.ClosedAt;
            // Fechar encerra a espera pelo solicitante: relógio pausado em chamado fechado não volta a andar.
            var pausedAt = close ? null : ticket.SlaPausedAt;

            await db.Tickets.Where(t => t.Id == ticket.Id).ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.CreatedAt, sw.CreatedAt)
                    .SetProperty(t => t.UpdatedAt, updatedAt)
                    .SetProperty(t => t.FirstRespondedAt, firstResponded)
                    .SetProperty(t => t.SlaResponseDueAt, responseDue)
                    .SetProperty(t => t.SlaResolutionDueAt, resolutionDue)
                    .SetProperty(t => t.Status, status)
                    .SetProperty(t => t.ResolvedAt, resolvedAt)
                    .SetProperty(t => t.ClosedAt, closedAt)
                    .SetProperty(t => t.SlaPausedAt, pausedAt),
                cancellationToken);
        }

        if (!dryRun)
        {
            logger.LogInformation(
                "Conciliação com o Spiceworks: {Matched} chamado(s) casado(s), {Dates} data(s) corrigida(s), {Closed} fechado(s).",
                matched, datesFixed, closed);
        }

        return new SpiceworksReconciliationReport(
            dryRun, total, total - exported.Count, matched, notFound, datesFixed, closed, stillOpen, notes);
    }

    private static void Note(List<ReconciliationNote> notes, int number, string? code, string message)
    {
        if (notes.Count < MaxNotes)
        {
            notes.Add(new ReconciliationNote(number, code, message));
        }
    }

    /// <summary>
    /// O Graph devolve o Message-ID entre &lt;&gt;, e o Spiceworks sem. A comparação ignora os
    /// sinais e a caixa das letras.
    /// </summary>
    private static string Key(string messageId) => messageId.Trim().Trim('<', '>').Trim().ToLowerInvariant();

    /// <summary>Lê a exportação: um objeto com a lista <c>tickets</c>.</summary>
    private static (int Total, List<SpiceworksTicket> Tickets) Parse(string content)
    {
        using var document = JsonDocument.Parse(content);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("tickets", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("o arquivo não é uma exportação do Spiceworks: falta a lista \"tickets\".");
        }

        var tickets = new List<SpiceworksTicket>();
        var total = 0;

        foreach (var item in list.EnumerateArray())
        {
            total++;

            var messageId = Text(item, "email_message_id");
            var created = Date(Text(item, "created_at"));

            if (string.IsNullOrWhiteSpace(messageId) || created is null)
            {
                continue;
            }

            tickets.Add(new SpiceworksTicket(
                (int)(Number(item, "ticket_number") ?? 0),
                messageId,
                string.Equals(Text(item, "status"), "closed", StringComparison.OrdinalIgnoreCase),
                created.Value,
                Date(Text(item, "closed_at")) ?? Date(Text(item, "status_changed_at")),
                Date(Text(item, "updated_at")),
                Number(item, "first_response_secs")));
        }

        return (total, tickets.OrderBy(t => t.Number).ToList());
    }

    /// <summary>Número, ou nulo quando o campo falta, vem nulo ou não é número.</summary>
    private static long? Number(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>O Spiceworks exporta "2025-01-14 13:10:39 UTC".</summary>
    private static DateTimeOffset? Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        if (text.EndsWith(" UTC", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
            ? new DateTimeOffset(utc, TimeSpan.Zero)
            : DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : null;
    }
}
