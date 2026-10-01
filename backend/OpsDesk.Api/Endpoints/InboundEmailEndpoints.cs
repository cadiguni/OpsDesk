using OpsDesk.Api.Authorization;
using OpsDesk.Api.Validation;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Common;
using OpsDesk.Application.Inbound;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// E-mails recebidos, caixa de SPAM e caixas monitoradas (docs/integracao-email.md).
///
/// Técnico e gestor veem e revisam o que chegou; o solicitante nem sabe que estas rotas
/// existem. Excluir mensagem e configurar caixas é só do gestor.
/// </summary>
public static class InboundEmailEndpoints
{
    public static IEndpointRouteBuilder MapInboundEmailEndpoints(this IEndpointRouteBuilder routes)
    {
        var inbound = routes
            .MapGroup("/api/inbound")
            .RequireAuthorization(AuthorizationPolicies.Staff)
            .WithTags("E-mails recebidos");

        inbound.MapGet("/", List).WithSummary("E-mails recebidos, com filtros e paginação.");
        inbound.MapGet("/{id:guid}", Get).WithSummary("Detalhe, com o corpo em texto puro.");

        inbound.MapPost("/{id:guid}/review", Review)
            .WithSummary("Revisão do SPAM: não é spam, liberar remetente, confirmar e bloquear, bloquear domínio.");

        inbound.MapPost("/{id:guid}/reprocess", Reprocess)
            .WithSummary("Processa de novo uma mensagem que falhou.");

        inbound.MapDelete("/{id:guid}", Delete)
            .RequireAuthorization(AuthorizationPolicies.Manager)
            .WithSummary("Exclui a mensagem. Só gestor.");

        inbound.MapGet("/rules", ListRules).WithSummary("Regras de liberação e bloqueio.");

        inbound.MapPost("/rules/{id:guid}/activation", SetRuleActive)
            .WithSummary("Liga ou desliga uma regra.");

        var accounts = routes
            .MapGroup("/api/admin/settings/email/accounts")
            .RequireAuthorization(AuthorizationPolicies.Manager)
            .WithTags("Configurações");

        accounts.MapGet("/", ListAccounts).WithSummary("Caixas monitoradas.");

        accounts.MapPost("/", CreateAccount)
            .ValidatingBody<SaveEmailAccountRequest>()
            .WithSummary("Passa a monitorar uma caixa.");

        accounts.MapPut("/{id:guid}", UpdateAccount)
            .ValidatingBody<SaveEmailAccountRequest>()
            .WithSummary("Altera uma caixa monitorada.");

        return routes;
    }

    private static async Task<IResult> List(
        InboundEmailService inbound,
        CancellationToken cancellationToken,
        InboundEmailStatus? status = null,
        string? search = null,
        DateTimeOffset? from = null,
        DateTimeOffset? before = null,
        int? page = null,
        int? pageSize = null) =>
        TypedResults.Ok(await inbound.ListAsync(
            new InboundEmailFilter(status, search, from, before) { Page = new PageRequest(page, pageSize) }, cancellationToken));

    private static async Task<IResult> Get(Guid id, InboundEmailService inbound, CancellationToken cancellationToken) =>
        await inbound.GetAsync(id, cancellationToken) is { } detail ? TypedResults.Ok(detail) : TypedResults.NotFound();

    private static async Task<IResult> Review(
        Guid id, ReviewSpamRequest request, InboundEmailService inbound, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        await inbound.ReviewAsync(id, request, currentUser.Viewer, cancellationToken) switch
        {
            SpamReviewResult.Reviewed reviewed => TypedResults.Ok(reviewed.Email),
            SpamReviewResult.NotFound => TypedResults.NotFound(),
            SpamReviewResult.NotInSpam notInSpam => TypedResults.Problem(
                detail: notInSpam.Message, statusCode: StatusCodes.Status409Conflict, title: "Fora do SPAM"),
            var other => throw new InvalidOperationException($"Resultado não tratado: {other.GetType().Name}.")
        };

    private static async Task<IResult> Reprocess(
        Guid id, MailboxSyncService sync, InboundEmailService inbound, CancellationToken cancellationToken)
    {
        if (await inbound.GetAsync(id, cancellationToken) is not { } detail)
        {
            return TypedResults.NotFound();
        }

        if (detail.Email.Status != InboundEmailStatus.Failed)
        {
            return TypedResults.Problem(
                detail: "Só mensagem que falhou é reprocessada.", statusCode: StatusCodes.Status409Conflict, title: "Não falhou");
        }

        await sync.ReprocessAsync(id, cancellationToken);

        return TypedResults.Ok((await inbound.GetAsync(id, cancellationToken))!.Email);
    }

    private static async Task<IResult> Delete(Guid id, InboundEmailService inbound, CancellationToken cancellationToken) =>
        await inbound.DeleteAsync(id, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<IResult> ListRules(InboundEmailService inbound, CancellationToken cancellationToken) =>
        TypedResults.Ok(await inbound.ListRulesAsync(cancellationToken));

    private static async Task<IResult> SetRuleActive(
        Guid id, RuleActivationRequest request, InboundEmailService inbound, CancellationToken cancellationToken) =>
        await inbound.SetRuleActiveAsync(id, request.IsActive, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<IResult> ListAccounts(EmailAccountService accounts, CancellationToken cancellationToken) =>
        TypedResults.Ok(await accounts.ListAsync(cancellationToken));

    private static async Task<IResult> CreateAccount(
        SaveEmailAccountRequest request, EmailAccountService accounts, CancellationToken cancellationToken) =>
        await accounts.SaveAsync(null, request, cancellationToken) is { } saved
            ? TypedResults.Created($"/api/admin/settings/email/accounts/{saved.Id}", saved)
            : AddressTaken();

    private static async Task<IResult> UpdateAccount(
        Guid id, SaveEmailAccountRequest request, EmailAccountService accounts, CancellationToken cancellationToken)
    {
        try
        {
            return await accounts.SaveAsync(id, request, cancellationToken) is { } saved ? TypedResults.Ok(saved) : AddressTaken();
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }

    private static IResult AddressTaken() => TypedResults.Problem(
        detail: "Essa caixa já está sendo monitorada.", statusCode: StatusCodes.Status409Conflict, title: "Caixa repetida");

    public record RuleActivationRequest(bool IsActive);
}
