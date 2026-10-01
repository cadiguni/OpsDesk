using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Authorization;
using OpsDesk.Application.Settings;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Inbound;

/// <summary>
/// Decide o destino de uma mensagem recebida (docs/integracao-email.md, seção 4): duplicata,
/// mensagem automática, spam, resposta a chamado existente ou chamado novo.
///
/// Roda num escopo próprio por mensagem, agindo em nome de quem a mandou (<see cref="IActorScope"/>):
/// o histórico e as notificações tratam a resposta por e-mail exatamente como a resposta
/// pelo portal — inclusive a regra de visibilidade. Quem manda "[OPS-000123]" no assunto sem
/// ser o solicitante nem da equipe não comenta no chamado alheio; a mensagem vira chamado
/// novo, dele.
/// </summary>
public partial class InboundEmailProcessor(
    IOpsDeskDbContext db,
    IActorScope actor,
    TicketService tickets,
    TicketCommentService comments,
    IClock clock,
    ILogger<InboundEmailProcessor> logger)
{
    public const int TitleMaxLength = 200;

    public async Task ProcessAsync(Guid inboundId, CancellationToken cancellationToken = default)
    {
        var email = await db.InboundEmails
            .Include(e => e.EmailAccount)
            .SingleOrDefaultAsync(e => e.Id == inboundId, cancellationToken);

        if (email is null || email.Status is not (InboundEmailStatus.Pending or InboundEmailStatus.Failed))
        {
            return;
        }

        var headers = email.HeadersJson is null
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(email.HeadersJson) ?? [];

        // 4.2 — reentrega do mesmo e-mail.
        if (email.MessageId is { } messageId && await db.InboundEmails.AnyAsync(
                e => e.Id != email.Id
                     && e.EmailAccountId == email.EmailAccountId
                     && e.MessageId == messageId
                     && e.Status != InboundEmailStatus.Duplicate,
                cancellationToken))
        {
            await FinishAsync(email, InboundEmailStatus.Duplicate, cancellationToken);
            return;
        }

        // 4.3 — resposta de férias, aviso de falha, robô.
        var automatic = AutomaticMessageDetector.Check(email.FromAddress, email.EmailAccount.Address, headers);

        if (automatic.Held)
        {
            await HoldAsync(email, automatic, cancellationToken);
            return;
        }

        // 4.6 — regras da equipe, domínio, autenticação, conteúdo.
        var rules = await db.SpamRules.AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken);
        IReadOnlyCollection<string>? internalDomains = null;

        if (email.EmailAccount.InternalDomainsOnly)
        {
            internalDomains = (await new SystemSettingsService(db).GetAsync(cancellationToken)).AllowedEmailDomains;
        }

        var spam = SpamClassifier.Classify(email.FromAddress, email.Subject, email.BodyText, headers, rules, internalDomains);

        if (spam.Held)
        {
            await HoldAsync(email, spam, cancellationToken);
            return;
        }

        email.SpamScore = spam.Score;

        await DeliverAsync(email, cancellationToken);
    }

    /// <summary>
    /// Entrega a mensagem — como resposta, ou como chamado novo. É também o que "não é spam"
    /// faz na caixa de SPAM: a revisão humana passa por cima da classificação, e não da
    /// regra de visibilidade.
    /// </summary>
    public async Task DeliverAsync(InboundEmail email, CancellationToken cancellationToken)
    {
        // 4.5 — quem mandou.
        var requester = await RequesterAsync(email, cancellationToken);

        if (requester is null)
        {
            await HoldAsync(email, new Verdict(true, 0, "Remetente com conta desativada"), cancellationToken);
            return;
        }

        actor.ActAs(requester.Id, requester.Role);
        var viewer = TicketViewer.From(requester);

        // 4.4 — resposta a chamado existente.
        var previousTicket = await LinkedTicketAsync(email, viewer, cancellationToken);

        if (previousTicket is { } linked)
        {
            var reply = ReplyTextExtractor.Extract(email.BodyText);
            var result = await comments.AddAsync(linked.Id, new AddCommentRequest(Truncate(reply, 10_000)), viewer, cancellationToken);

            if (result is AddCommentResult.Added)
            {
                email.TicketId = linked.Id;
                await FinishAsync(email, InboundEmailStatus.Processed, cancellationToken);
                return;
            }

            // Fechado há mais tempo que a janela, ou cancelado: a resposta vira chamado novo,
            // que cita o antigo para a equipe ligar os pontos.
            logger.LogInformation(
                "Resposta por e-mail ao {Code} não coube no chamado ({Result}); abrindo chamado novo.",
                linked.Code, result.GetType().Name);
        }

        // Reprocessamento depois de uma falha no meio do caminho: o chamado pode já existir.
        if (email.MessageId is { } messageId
            && await db.Tickets.Where(t => t.Source == TicketSource.Email && t.ExternalId == messageId)
                .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken) is { } existing)
        {
            email.TicketId = existing;
            await FinishAsync(email, InboundEmailStatus.Processed, cancellationToken);
            return;
        }

        // 4.7 — chamado novo.
        var title = CleanSubject(email.Subject) is { Length: > 0 } clean
            ? Truncate(clean, TitleMaxLength)
            : "Chamado sem assunto";

        var description = string.IsNullOrWhiteSpace(email.BodyText) ? "(mensagem sem texto)" : email.BodyText.Trim();

        if (previousTicket is { } old)
        {
            description = $"Resposta por e-mail ao chamado {old.Code}, que já estava encerrado.\n\n{description}";
        }

        var category = email.EmailAccount.DefaultCategoryId is { } configured
                       && await db.Categories.AnyAsync(c => c.Id == configured && c.IsActive, cancellationToken)
            ? configured
            : await FallbackCategoryAsync(cancellationToken);

        var ticket = await tickets.CreateFromEmailAsync(
            requester.Id, title, Truncate(description, 20_000), category, email.EmailAccount.DefaultPriority, email.MessageId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Não há política de SLA ativa para a prioridade {email.EmailAccount.DefaultPriority}.");

        email.TicketId = ticket.Id;
        await FinishAsync(email, InboundEmailStatus.Processed, cancellationToken);

        logger.LogInformation("E-mail de {From} virou o chamado {Code}.", email.FromAddress, ticket.Code);
    }

    /// <summary>
    /// O solicitante, pelo endereço do remetente. Desconhecido vira usuário sem senha: é
    /// dono dos chamados que abriu por e-mail e os reencontra no portal quando definir uma
    /// senha por "esqueci minha senha". Conta desativada não abre chamado.
    /// </summary>
    private async Task<User?> RequesterAsync(InboundEmail email, CancellationToken cancellationToken)
    {
        var address = email.FromAddress.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == address, cancellationToken);

        if (user is not null)
        {
            return user.IsActive ? user : null;
        }

        var name = string.IsNullOrWhiteSpace(email.FromName) ? address.Split('@')[0] : email.FromName.Trim();

        user = new User { Name = Truncate(name, 200), Email = address, Role = UserRole.Requester };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return user;
    }

    /// <summary>
    /// O chamado a que a mensagem responde: primeiro pelos cabeçalhos — uma mensagem já
    /// recebida no mesmo fio —, e depois pelo código no assunto, que é o que as
    /// notificações do OpsDesk levam. Só devolve chamado que o remetente enxerga.
    /// </summary>
    private async Task<Ticket?> LinkedTicketAsync(InboundEmail email, TicketViewer viewer, CancellationToken cancellationToken)
    {
        var referenced = $"{email.InReplyTo} {email.References}"
            .Split([' ', '\t', '\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToList();

        Guid? ticketId = null;

        if (referenced.Count > 0)
        {
            ticketId = await db.InboundEmails
                .Where(e => e.TicketId != null && e.MessageId != null && referenced.Contains(e.MessageId))
                .OrderByDescending(e => e.ReceivedAt)
                .Select(e => e.TicketId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (ticketId is null && TicketCodeInSubject().Match(email.Subject) is { Success: true } match)
        {
            var code = match.Groups[1].Value.ToUpperInvariant();
            ticketId = await db.Tickets.Where(t => t.Code == code).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
        }

        return ticketId is { } id
            ? await db.Tickets.AsNoTracking().VisibleTo(viewer).SingleOrDefaultAsync(t => t.Id == id, cancellationToken)
            : null;
    }

    private async Task<Guid> FallbackCategoryAsync(CancellationToken cancellationToken) =>
        await db.Categories.Where(c => c.IsActive && c.Name == "Outros").Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken)
        ?? await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => c.Id).FirstAsync(cancellationToken);

    private async Task HoldAsync(InboundEmail email, Verdict verdict, CancellationToken cancellationToken)
    {
        email.SpamScore = verdict.Score;
        email.SpamReason = verdict.Reason;
        await FinishAsync(email, InboundEmailStatus.Spam, cancellationToken);
    }

    private async Task FinishAsync(InboundEmail email, InboundEmailStatus status, CancellationToken cancellationToken)
    {
        email.Status = status;
        email.ProcessedAt = clock.UtcNow;
        email.ProcessingError = null;
        email.NextAttemptAt = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>
    /// O assunto como título: sem o código do chamado e sem os prefixos de resposta e
    /// encaminhamento que os clientes de e-mail empilham — senão todo chamado reaberto por
    /// e-mail começaria com "RE:".
    /// </summary>
    public static string CleanSubject(string subject)
    {
        var text = TicketCodeInSubject().Replace(subject, " ");

        string previous;
        do
        {
            previous = text;
            text = ReplyPrefix().Replace(text.Trim(), "");
        }
        while (text != previous);

        return MultipleSpaces().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\[(OPS-\d{6,})\]", RegexOptions.IgnoreCase)]
    private static partial Regex TicketCodeInSubject();

    [GeneratedRegex(@"^(re|res|resp|fw|fwd|enc|tr|aw|wg)\s*(\[\d+\])?\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ReplyPrefix();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();
}
