using System.Net;
using System.Text;
using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Domain.Tickets;

namespace OpsDesk.Application.Notifications;

/// <summary>Pessoa envolvida no chamado, como o planejador precisa enxergá-la.</summary>
public record NotificationPerson(Guid Id, string Name, string Email, UserRole Role, bool IsActive)
{
    public TicketViewer Viewer => new(Id, Role);
}

/// <summary>Comentário gravado junto com a mudança.</summary>
public record AddedComment(Guid AuthorId, string AuthorName, string Content, bool IsInternal);

/// <summary>
/// O que mudou num chamado numa única gravação. <see cref="PreviousStatus"/> e
/// <see cref="PreviousAssignee"/> só vêm preenchidos quando o campo mudou.
/// </summary>
public record TicketChange(
    Guid TicketId,
    string Code,
    string Title,
    NotificationPerson Requester,
    NotificationPerson? CurrentAssignee,
    bool AssigneeChanged,
    NotificationPerson? PreviousAssignee,
    TicketStatus CurrentStatus,
    TicketStatus? PreviousStatus,
    IReadOnlyList<AddedComment> Comments);

/// <summary>Canal de e-mail ligado. Nulo quando o envio está desligado.</summary>
public record EmailChannel(string? PortalUrl);

public record PlannedNotifications(IReadOnlyList<Notification> InApp, IReadOnlyList<OutboundEmail> Emails);

/// <summary>
/// Quem é avisado de quê (README, seção 18, versão 1.2). Função pura: recebe o que mudou
/// e devolve as linhas a gravar, sem tocar em banco — é aqui que as regras moram, e é
/// aqui que os testes as prendem.
///
/// Três regras de destinatário:
///
/// <list type="bullet">
/// <item>o <b>solicitante</b> recebe e-mail de comentário público e de mudança de status;</item>
/// <item>o <b>responsável</b> recebe notificação no portal — o gestor, só quando o chamado
/// está no nome dele, porque é a mesma regra;</item>
/// <item>ninguém é avisado da própria ação.</item>
/// </list>
///
/// E a invariante 2 em canal novo: nota interna nunca entra em e-mail, e cada destinatário
/// passa pela mesma regra de visibilidade que a API usa.
/// </summary>
public static class NotificationPlanner
{
    public static PlannedNotifications Plan(
        TicketChange change, Guid? actorId, DateTimeOffset now, EmailChannel? email)
    {
        var inApp = new List<Notification>();
        var emails = new List<OutboundEmail>();

        PlanInApp(change, actorId, now, inApp);

        if (email is not null && EmailFor(change, actorId, now, email) is { } message)
        {
            emails.Add(message);
        }

        return new PlannedNotifications(inApp, emails);
    }

    // ----- Portal: o responsável -----

    private static void PlanInApp(
        TicketChange change, Guid? actorId, DateTimeOffset now, List<Notification> into)
    {
        if (change.AssigneeChanged)
        {
            if (Reachable(change.CurrentAssignee, change, actorId) is { } assignee)
            {
                into.Add(New(assignee, change, NotificationKind.Assigned, actorId, now));
            }

            // Quem perdeu o chamado precisa saber que não é mais com ele — senão continua
            // contando com um trabalho que foi para outra pessoa.
            if (Reachable(change.PreviousAssignee, change, actorId) is { } previous)
            {
                into.Add(New(previous, change, NotificationKind.Unassigned, actorId, now));
            }

            // "Passou a ser seu" já leva a pessoa ao chamado; comentário e status da mesma
            // gravação ela vê lá. Uma notificação por gesto, não uma por campo.
            return;
        }

        if (Reachable(change.CurrentAssignee, change, actorId) is not { } responsible)
        {
            return;
        }

        var visibleComments = change.Comments
            .Where(c => c.AuthorId != responsible.Id && CanSee(responsible, change, c))
            .ToList();

        var statusChanged = change.PreviousStatus is { } from && from != change.CurrentStatus;

        // Da mais para a menos informativa. Reabertura diz mais que "o status mudou", e
        // "o solicitante respondeu" diz mais que "alguém comentou".
        if (statusChanged && TicketStatusMachine.IsReopening(change.PreviousStatus!.Value, change.CurrentStatus))
        {
            into.Add(New(responsible, change, NotificationKind.Reopened, actorId, now));
        }
        else if (visibleComments.Any(c => c.AuthorId == change.Requester.Id))
        {
            into.Add(New(responsible, change, NotificationKind.RequesterReplied, actorId, now));
        }
        else if (visibleComments.Count > 0)
        {
            into.Add(New(responsible, change, NotificationKind.CommentAdded, actorId, now));
        }
        else if (statusChanged)
        {
            into.Add(New(responsible, change, NotificationKind.StatusChanged, actorId, now,
                detail: change.CurrentStatus.ToString()));
        }
    }

    /// <summary>
    /// A pessoa, se ela deve ser avisada: existe, está ativa, não foi quem agiu, e enxerga
    /// o chamado pela regra de visibilidade da API.
    /// </summary>
    private static NotificationPerson? Reachable(
        NotificationPerson? person, TicketChange change, Guid? actorId) =>
        person is { IsActive: true } && person.Id != actorId && CanSeeTicket(person, change)
            ? person
            : null;

    private static bool CanSeeTicket(NotificationPerson person, TicketChange change) =>
        person.Viewer.IsStaff || person.Id == change.Requester.Id;

    private static bool CanSee(NotificationPerson person, TicketChange change, AddedComment comment) =>
        CanSeeTicket(person, change) && (!comment.IsInternal || person.Viewer.IsStaff);

    private static Notification New(
        NotificationPerson recipient,
        TicketChange change,
        NotificationKind kind,
        Guid? actorId,
        DateTimeOffset now,
        string? detail = null) => new()
        {
            UserId = recipient.Id,
            TicketId = change.TicketId,
            Kind = kind,
            ActorId = actorId,
            Detail = detail,
            CreatedAt = now
        };

    // ----- E-mail: o solicitante -----

    private static OutboundEmail? EmailFor(
        TicketChange change, Guid? actorId, DateTimeOffset now, EmailChannel channel)
    {
        var requester = change.Requester;

        if (!requester.IsActive || string.IsNullOrWhiteSpace(requester.Email) || requester.Id == actorId)
        {
            return null;
        }

        // Nunca nota interna, venha de quem vier — nem para solicitante que seja da equipe.
        // A regra do README é sobre o canal, não sobre a pessoa: caixa de e-mail é
        // encaminhada, impressa e lida no celular de outra pessoa.
        var replies = change.Comments
            .Where(c => !c.IsInternal && c.AuthorId != requester.Id)
            .ToList();

        var statusChanged = change.PreviousStatus is { } from && from != change.CurrentStatus;

        if (replies.Count == 0 && !statusChanged)
        {
            return null;
        }

        var subject = replies.Count > 0
            ? $"[{change.Code}] Nova resposta no seu chamado: {change.Title}"
            : $"[{change.Code}] {StatusSubject(change.CurrentStatus)}: {change.Title}";

        return new OutboundEmail
        {
            TicketId = change.TicketId,
            ToAddress = requester.Email,
            ToName = requester.Name,
            Subject = SingleLine(subject),
            HtmlBody = Body(change, replies, statusChanged, channel),
            Status = OutboundEmailStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now
        };
    }

    private static string Body(
        TicketChange change, IReadOnlyList<AddedComment> replies, bool statusChanged, EmailChannel channel)
    {
        var html = new StringBuilder();

        html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1f2937\">");
        html.Append($"<p>Olá, {Encode(change.Requester.Name)}.</p>");
        html.Append($"<p>Há novidade no seu chamado <strong>{Encode(change.Code)}</strong> — {Encode(change.Title)}.</p>");

        foreach (var reply in replies)
        {
            html.Append($"<p style=\"margin-bottom:4px\"><strong>{Encode(reply.AuthorName)}</strong> respondeu:</p>");

            // Texto do usuário, sempre codificado: um comentário com "<a href=...>" viraria
            // link clicável no e-mail de outra pessoa.
            html.Append("<div style=\"white-space:pre-wrap;border-left:3px solid #d1d5db;padding:4px 12px;margin:0 0 12px\">");
            html.Append(Encode(reply.Content));
            html.Append("</div>");
        }

        if (statusChanged)
        {
            html.Append($"<p>Situação do chamado: <strong>{Encode(StatusLabel(change.CurrentStatus))}</strong>.</p>");

            if (StatusHint(change.CurrentStatus) is { } hint)
            {
                html.Append($"<p>{Encode(hint)}</p>");
            }
        }

        if (!string.IsNullOrWhiteSpace(channel.PortalUrl))
        {
            var link = $"{channel.PortalUrl.TrimEnd('/')}/chamados/{change.TicketId}";
            html.Append($"<p><a href=\"{Encode(link)}\">Abrir o chamado no portal</a></p>");
        }

        // Até a versão 2.0 ninguém lê a caixa de suporte: uma resposta por e-mail se
        // perderia sem que a pessoa soubesse. O aviso sai quando a ingestão existir.
        html.Append("<p style=\"color:#6b7280;font-size:12px\">Mensagem automática do OpsDesk. " +
                    "Respostas a este e-mail não são lidas — para responder, use o portal.</p>");
        html.Append("</div>");

        return html.ToString();
    }

    private static string StatusSubject(TicketStatus status) => status switch
    {
        TicketStatus.Triage => "Chamado em triagem",
        TicketStatus.InProgress => "Chamado em atendimento",
        TicketStatus.WaitingOnRequester => "Chamado aguardando sua resposta",
        TicketStatus.Resolved => "Chamado resolvido",
        TicketStatus.Closed => "Chamado fechado",
        TicketStatus.Cancelled => "Chamado cancelado",
        _ => "Chamado atualizado"
    };

    public static string StatusLabel(TicketStatus status) => status switch
    {
        TicketStatus.Open => "Aberto",
        TicketStatus.Triage => "Em triagem",
        TicketStatus.InProgress => "Em atendimento",
        TicketStatus.WaitingOnRequester => "Aguardando você",
        TicketStatus.Resolved => "Resolvido",
        TicketStatus.Closed => "Fechado",
        TicketStatus.Cancelled => "Cancelado",
        _ => status.ToString()
    };

    private static string? StatusHint(TicketStatus status) => status switch
    {
        TicketStatus.WaitingOnRequester => "A equipe precisa de uma resposta sua para continuar.",
        TicketStatus.Resolved => "Se o problema continua, responda pelo portal e o chamado é reaberto. " +
                                 "Se está tudo certo, você pode confirmar e fechar.",
        _ => null
    };

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    /// <summary>Assunto numa linha só: título com quebra de linha não pode virar cabeçalho.</summary>
    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');
}
