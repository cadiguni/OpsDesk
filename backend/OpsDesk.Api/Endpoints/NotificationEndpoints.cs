using OpsDesk.Api.Authorization;
using OpsDesk.Api.Validation;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Common;
using OpsDesk.Application.Notifications;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// Notificações no portal e configuração do envio de e-mail (README, seção 18, 1.2).
///
/// As notificações são de quem está autenticado, e de mais ninguém: o filtro por dono está
/// no serviço, não na rota. A configuração de e-mail é só de gestor.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder routes)
    {
        var notifications = routes
            .MapGroup("/api/notifications")
            .RequireAuthorization()
            .WithTags("Notificações");

        notifications.MapGet("/", List)
            .WithSummary("Notificações do usuário autenticado, mais recentes primeiro.");

        notifications.MapGet("/unread-count", CountUnread)
            .WithSummary("Quantas não lidas, para o sino.");

        notifications.MapPost("/{id:guid}/read", MarkRead)
            .WithSummary("Marca uma notificação como lida.");

        notifications.MapPost("/read-all", MarkAllRead)
            .WithSummary("Marca todas como lidas.");

        var email = routes
            .MapGroup("/api/admin/settings/email")
            .RequireAuthorization(AuthorizationPolicies.Manager)
            .WithTags("Configurações");

        email.MapGet("/", GetEmailSettings)
            .WithSummary("Configuração do envio de e-mail. O client secret nunca é devolvido.");

        email.MapPut("/", UpdateEmailSettings)
            .ValidatingBody<UpdateEmailSettingsRequest>()
            .WithSummary("Grava a configuração. Secret vazio mantém o atual.");

        email.MapPost("/test", SendTestEmail)
            .ValidatingBody<SendTestEmailRequest>()
            .WithSummary("Envia um e-mail de teste agora, com a configuração gravada.");

        return routes;
    }

    private static async Task<IResult> List(
        NotificationService notifications,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        bool unreadOnly = false,
        int? page = null,
        int? pageSize = null) =>
        TypedResults.Ok(await notifications.ListAsync(
            currentUser.Viewer, unreadOnly, new PageRequest(page, pageSize), cancellationToken));

    private static async Task<IResult> CountUnread(
        NotificationService notifications, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        TypedResults.Ok(await notifications.CountUnreadAsync(currentUser.Viewer, cancellationToken));

    private static async Task<IResult> MarkRead(
        Guid id, NotificationService notifications, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        // 404 também para notificação de outra pessoa: 403 confirmaria que ela existe.
        await notifications.MarkReadAsync(id, currentUser.Viewer, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<IResult> MarkAllRead(
        NotificationService notifications, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        await notifications.MarkAllReadAsync(currentUser.Viewer, cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetEmailSettings(
        EmailSettingsService settings, CancellationToken cancellationToken) =>
        TypedResults.Ok(await settings.GetAsync(cancellationToken));

    private static async Task<IResult> UpdateEmailSettings(
        UpdateEmailSettingsRequest request,
        EmailSettingsService settings,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var result = await settings.UpdateAsync(request, currentUser.Viewer.UserId, cancellationToken);

        return result switch
        {
            UpdateEmailSettingsResult.Saved saved => TypedResults.Ok(saved.Settings),

            UpdateEmailSettingsResult.Incomplete incomplete => TypedResults.Problem(
                detail: incomplete.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Configuração incompleta"),

            _ => throw new InvalidOperationException(
                $"Resultado de configuração não tratado: {result.GetType().Name}.")
        };
    }

    private static async Task<IResult> SendTestEmail(
        SendTestEmailRequest request, EmailSettingsService settings, CancellationToken cancellationToken)
    {
        var result = await settings.SendTestAsync(request, cancellationToken);

        return result switch
        {
            SendTestEmailResult.Sent => TypedResults.NoContent(),

            SendTestEmailResult.NotConfigured notConfigured => TypedResults.Problem(
                detail: notConfigured.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Envio não configurado"),

            // 502: o pedido estava certo e a configuração existe; quem falhou foi o
            // provedor, ou as credenciais que ele recusou. A mensagem dele vai no detalhe.
            SendTestEmailResult.Failed failed => TypedResults.Problem(
                detail: failed.Message,
                statusCode: StatusCodes.Status502BadGateway,
                title: "O envio falhou"),

            _ => throw new InvalidOperationException(
                $"Resultado de teste não tratado: {result.GetType().Name}.")
        };
    }
}
