using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Sla;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Import;

/// <summary>De onde vem o arquivo. Cada origem tem os seus nomes de coluna e de status.</summary>
public enum ImportFormat
{
    /// <summary>CSV ou JSON no formato do próprio OpsDesk (colunas em português).</summary>
    OpsDesk,

    /// <summary>Exportação de chamados do Freshdesk, em CSV.</summary>
    Freshdesk,

    /// <summary>Exportação de chamados do Freshservice, em CSV.</summary>
    Freshservice
}

public record ImportIssue(int Line, string Message);

/// <summary>Resultado da conferência ou da importação.</summary>
public record ImportReport(
    bool DryRun,
    int Rows,
    int Valid,
    int Created,
    int AlreadyImported,
    int NewRequesters,
    IReadOnlyList<string> RecognizedColumns,
    IReadOnlyList<string> MissingColumns,
    IReadOnlyList<ImportIssue> Issues);

/// <summary>
/// Importação de chamados de outra ferramenta (README, seção 18, versão 2.0): CSV ou JSON
/// no formato do OpsDesk, ou a exportação em CSV do Freshdesk e do Freshservice.
///
/// Regras:
///
/// <list type="bullet">
/// <item><b>sempre conferir antes</b> — a mesma chamada com <c>dryRun</c> lê, valida e
/// conta sem gravar nada, e é o que a tela mostra antes do botão de importar;</item>
/// <item><b>importar duas vezes não duplica</b> — o identificador de origem vai para
/// <c>ExternalId</c>, e linha já importada é pulada;</item>
/// <item><b>solicitante desconhecido vira usuário sem senha</b>, como na caixa de e-mail:
/// ele reencontra os chamados ao definir uma senha;</item>
/// <item><b>chamado importado fica fora dos indicadores de SLA</b> (<c>TicketSla</c>): o
/// prazo do OpsDesk não existia quando ele foi atendido;</item>
/// <item>datas sem fuso são lidas no fuso do expediente.</item>
/// </list>
/// </summary>
public class TicketImportService(
    IOpsDeskDbContext db,
    IBusinessCalendar calendar,
    SlaClock sla,
    ILogger<TicketImportService> logger)
{
    public const int MaxRows = 5000;

    public const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>Nomes aceitos para cada campo, por origem. A comparação ignora maiúsculas.</summary>
    private static readonly Dictionary<ImportFormat, Dictionary<string, string[]>> Columns = new()
    {
        [ImportFormat.OpsDesk] = new()
        {
            ["id"] = ["codigo_externo", "id"],
            ["title"] = ["titulo", "título"],
            ["description"] = ["descricao", "descrição"],
            ["email"] = ["solicitante_email"],
            ["name"] = ["solicitante_nome"],
            ["category"] = ["categoria"],
            ["priority"] = ["prioridade"],
            ["status"] = ["status"],
            ["agent"] = ["responsavel_email", "responsável_email"],
            ["created"] = ["criado_em"],
            ["resolved"] = ["resolvido_em"],
            ["closed"] = ["fechado_em"]
        },
        [ImportFormat.Freshdesk] = new()
        {
            ["id"] = ["Ticket ID"],
            ["title"] = ["Subject"],
            ["description"] = ["Description"],
            ["email"] = ["Requester Email", "Email", "Contact Email"],
            ["name"] = ["Requester Name", "Full name", "Contact"],
            ["category"] = ["Type", "Category"],
            ["priority"] = ["Priority"],
            ["status"] = ["Status"],
            ["agent"] = ["Agent"],
            ["created"] = ["Created time"],
            ["resolved"] = ["Resolved time"],
            ["closed"] = ["Closed time"]
        },
        [ImportFormat.Freshservice] = new()
        {
            ["id"] = ["Ticket Id", "Ticket ID"],
            ["title"] = ["Subject"],
            ["description"] = ["Description"],
            ["email"] = ["Requester Email", "Requester Emails", "Email"],
            ["name"] = ["Requester Name", "Requester"],
            ["category"] = ["Category"],
            ["priority"] = ["Priority"],
            ["status"] = ["Status"],
            ["agent"] = ["Agent"],
            ["created"] = ["Created Date", "Created Time"],
            ["resolved"] = ["Resolved Date", "Resolved Time"],
            ["closed"] = ["Closed Date", "Closed Time"]
        }
    };

    private static readonly string[] Required = ["title", "email"];

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm",
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy"
    ];

    public async Task<ImportReport> ImportAsync(
        string content, bool isJson, ImportFormat format, bool dryRun, CancellationToken cancellationToken = default)
    {
        var issues = new List<ImportIssue>();
        List<(int Line, Dictionary<string, string> Fields)> rows;

        try
        {
            rows = isJson ? ParseJson(content) : CsvTable.Parse(content);
        }
        catch (JsonException ex)
        {
            return Empty(dryRun, [new ImportIssue(0, $"JSON inválido: {ex.Message}")]);
        }

        if (rows.Count > MaxRows)
        {
            return Empty(dryRun, [new ImportIssue(0, $"O arquivo tem {rows.Count} linhas; o limite é {MaxRows} por importação. Divida o arquivo.")]);
        }

        var names = Columns[format];
        var present = rows.SelectMany(r => r.Fields.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var recognized = names
            .Where(n => n.Value.Any(alias => present.Contains(alias, StringComparer.OrdinalIgnoreCase)))
            .Select(n => n.Key)
            .ToList();

        var missing = Required.Where(r => !recognized.Contains(r)).Select(r => names[r][0]).ToList();

        if (missing.Count > 0)
        {
            return new ImportReport(dryRun, rows.Count, 0, 0, 0, 0, recognized, missing,
                [new ImportIssue(0, $"Faltam colunas obrigatórias: {string.Join(", ", missing)}.")]);
        }

        var source = format switch
        {
            ImportFormat.Freshdesk => TicketSource.Freshdesk,
            ImportFormat.Freshservice => TicketSource.Freshservice,
            _ => isJson ? TicketSource.ExternalApi : TicketSource.Csv
        };

        var categories = await db.Categories.AsNoTracking().ToListAsync(cancellationToken);
        var fallbackCategory = categories.FirstOrDefault(c => c.Name == "Outros" && c.IsActive) ?? categories.First(c => c.IsActive);
        var policies = await db.SlaPolicies.AsNoTracking().Where(p => p.IsActive).ToDictionaryAsync(p => p.Priority, cancellationToken);
        var staff = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == UserRole.Technician || u.Role == UserRole.Manager))
            .Select(u => new { u.Id, u.Email, u.Name })
            .ToListAsync(cancellationToken);
        var importedIds = (await db.Tickets.AsNoTracking()
                .Where(t => t.Source == source && t.ExternalId != null)
                .Select(t => t.ExternalId!)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var users = new Dictionary<string, User>();
        int valid = 0, created = 0, already = 0, newRequesters = 0;

        foreach (var (line, fields) in rows)
        {
            string? Get(string field) =>
                names[field].Select(alias => fields.GetValueOrDefault(alias)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

            var title = Get("title");
            var email = Get("email")?.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(title))
            {
                issues.Add(new ImportIssue(line, "Sem título."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                issues.Add(new ImportIssue(line, "Sem e-mail do solicitante válido."));
                continue;
            }

            var externalId = Get("id");

            if (externalId is not null && !importedIds.Add(externalId))
            {
                already++;
                continue;
            }

            var createdAt = ParseDate(Get("created"), out var dateError);
            var resolvedAt = ParseDate(Get("resolved"), out var resolvedError);
            var closedAt = ParseDate(Get("closed"), out var closedError);

            if ((dateError ?? resolvedError ?? closedError) is { } error)
            {
                issues.Add(new ImportIssue(line, error));
                continue;
            }

            var priority = Priority(Get("priority"));
            var status = Status(Get("status"), closedAt, resolvedAt);

            if (priority is null || status is null)
            {
                issues.Add(new ImportIssue(line, priority is null
                    ? $"Prioridade desconhecida: \"{Get("priority")}\"."
                    : $"Status desconhecido: \"{Get("status")}\"."));
                continue;
            }

            valid++;

            if (dryRun)
            {
                if (!users.ContainsKey(email) && !await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
                {
                    users[email] = new User();
                    newRequesters++;
                }

                continue;
            }

            if (!users.TryGetValue(email, out var requester))
            {
                requester = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

                if (requester is null)
                {
                    requester = new User
                    {
                        Email = email,
                        Name = Truncate(Get("name") is { Length: > 0 } name ? name : email.Split('@')[0], 200),
                        Role = UserRole.Requester
                    };
                    db.Users.Add(requester);
                    newRequesters++;
                }

                users[email] = requester;
            }

            var opened = createdAt ?? DateTimeOffset.UtcNow;
            var deadlines = sla.Calculate(opened, policies[priority.Value]);
            var categoryName = Get("category");
            // O Freshdesk exporta o agente pelo nome. Nome que dois técnicos compartilham não
            // é adivinhado: o chamado entra sem responsável, e a equipe atribui depois.
            var agent = Get("agent");
            var byName = staff.Where(s => s.Name.Equals(agent, StringComparison.OrdinalIgnoreCase)).ToList();
            var assignee = agent is null
                ? null
                : staff.FirstOrDefault(s => s.Email.Equals(agent, StringComparison.OrdinalIgnoreCase))
                  ?? (byName.Count == 1 ? byName[0] : null);

            var description = Get("description") ?? "";
            var origin = format == ImportFormat.OpsDesk ? "arquivo" : format.ToString();
            var note = externalId is null ? $"Importado de {origin}." : $"Importado de {origin} (#{externalId}).";

            var finished = status is TicketStatus.Resolved or TicketStatus.Closed;

            db.Tickets.Add(new Ticket
            {
                Title = Truncate(title, 200),
                Description = Truncate(string.IsNullOrWhiteSpace(description) ? note : $"{description}\n\n— {note}", 20_000),
                CategoryId = categories.FirstOrDefault(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase))?.Id
                             ?? fallbackCategory.Id,
                Priority = priority.Value,
                Status = status.Value,
                RequesterId = requester.Id,
                AssignedTechnicianId = assignee?.Id,
                Source = source,
                ExternalId = externalId is null ? null : Truncate(externalId, 500),
                CreatedAt = opened,
                SlaResponseDueAt = deadlines.ResponseDueAt,
                SlaResolutionDueAt = deadlines.ResolutionDueAt,
                ResolvedAt = finished ? resolvedAt ?? closedAt : null,
                ClosedAt = status == TicketStatus.Closed ? closedAt ?? resolvedAt : null
            });

            created++;

            // Em lotes: cinco mil chamados numa gravação só seguraria a transação e a
            // memória do contexto por tempo demais.
            if (created % 200 == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        if (!dryRun)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Importação {Format}: {Created} chamado(s), {Users} solicitante(s) novo(s).", format, created, newRequesters);
        }

        return new ImportReport(dryRun, rows.Count, valid, created, already, newRequesters, recognized, [], issues.Take(200).ToList());
    }

    /// <summary>
    /// Data com fuso explícito vale como está; sem fuso, é hora de São Paulo. As exportações
    /// do Freshdesk e do Freshservice saem no fuso da conta, sem indicá-lo — e a conta de uma
    /// empresa daqui está no fuso daqui.
    /// </summary>
    private DateTimeOffset? ParseDate(string? value, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return calendar.FromLocal(local);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
        {
            return withOffset.ToUniversalTime();
        }

        error = $"Data em formato desconhecido: \"{value}\". Use 2026-09-30 14:00 ou 30/09/2026 14:00.";
        return null;
    }

    private static TicketPriority? Priority(string? value) => Normalize(value) switch
    {
        null or "" => TicketPriority.Medium,
        "low" or "baixa" => TicketPriority.Low,
        "medium" or "media" or "normal" => TicketPriority.Medium,
        "high" or "alta" => TicketPriority.High,
        "urgent" or "critical" or "critica" or "urgente" => TicketPriority.Critical,
        _ => null
    };

    private static TicketStatus? Status(string? value, DateTimeOffset? closedAt, DateTimeOffset? resolvedAt) => Normalize(value) switch
    {
        null or "" => closedAt is not null ? TicketStatus.Closed : resolvedAt is not null ? TicketStatus.Resolved : TicketStatus.Open,
        "open" or "aberto" or "new" or "novo" => TicketStatus.Open,
        "triage" or "em triagem" => TicketStatus.Triage,
        "inprogress" or "in progress" or "em atendimento" or "waiting on third party" => TicketStatus.InProgress,
        "pending" or "waiting on customer" or "waitingonrequester" or "aguardando usuario" => TicketStatus.WaitingOnRequester,
        "resolved" or "resolvido" => TicketStatus.Resolved,
        "closed" or "fechado" => TicketStatus.Closed,
        "cancelled" or "canceled" or "cancelado" => TicketStatus.Cancelled,
        _ => null
    };

    /// <summary>Minúsculas e sem acento, para "Média" e "media" serem a mesma coisa.</summary>
    private static string? Normalize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);

        return new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static List<(int Line, Dictionary<string, string> Fields)> ParseJson(string content)
    {
        using var document = JsonDocument.Parse(content);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("o arquivo precisa ser uma lista de chamados: [ { ... }, { ... } ].");
        }

        return document.RootElement.EnumerateArray()
            .Select((item, index) => (index + 1, item.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString(),
                StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    private static ImportReport Empty(bool dryRun, List<ImportIssue> issues) => new(dryRun, 0, 0, 0, 0, 0, [], [], issues);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
