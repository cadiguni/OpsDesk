using OpsDesk.Api.Authorization;
using OpsDesk.Api.Validation;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Common;
using OpsDesk.Application.Users;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// Administração de usuários (README, seção 13.9). Só gestor.
///
/// Rota própria, e não mais parâmetros em <c>/api/users</c>: aquela é o catálogo de gente
/// ativa para os seletores da equipe, com teto fixo de linhas; esta lista inativos,
/// pagina, e é a porta de entrada para mudar perfil — acesso diferente, contrato diferente.
/// </summary>
public static class UserAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapUserAdministrationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/admin/users")
            .RequireAuthorization(AuthorizationPolicies.Manager)
            .WithTags("Administração de usuários");

        group.MapGet("/", List)
            .WithSummary("Usuários, ativos e inativos, com filtros e paginação.");

        group.MapPost("/{id:guid}/role", ChangeRole)
            .ValidatingBody<ChangeRoleRequest>()
            .WithSummary("Troca o perfil. Rebaixar derruba as sessões da pessoa.");

        group.MapPost("/{id:guid}/activation", ChangeActivation)
            .WithSummary("Desativa ou reativa a conta. Desativar derruba as sessões da pessoa.");

        return routes;
    }

    private static async Task<IResult> List(
        UserAdministrationService users,
        CancellationToken cancellationToken,
        string? search = null,
        UserRole? role = null,
        bool? isActive = null,
        int? page = null,
        int? pageSize = null)
    {
        var filter = new ManagedUserFilter(search, role, isActive)
        {
            Page = new PageRequest(page, pageSize)
        };

        return TypedResults.Ok(await users.ListAsync(filter, cancellationToken));
    }

    private static async Task<IResult> ChangeRole(
        Guid id,
        ChangeRoleRequest request,
        UserAdministrationService users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        ToResult(await users.ChangeRoleAsync(id, request, currentUser.Viewer, cancellationToken));

    private static async Task<IResult> ChangeActivation(
        Guid id,
        ChangeActivationRequest request,
        UserAdministrationService users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        ToResult(await users.ChangeActivationAsync(id, request, currentUser.Viewer, cancellationToken));

    private static IResult ToResult(ChangeUserResult result) => result switch
    {
        ChangeUserResult.Changed changed => TypedResults.Ok(changed.User),

        ChangeUserResult.UserNotFound => TypedResults.NotFound(),

        ChangeUserResult.NotAllowed => TypedResults.Problem(
            detail: "Apenas gestores ativos administram usuários.",
            statusCode: StatusCodes.Status403Forbidden,
            title: "Administração não permitida"),

        // 409 e não 403: o gestor tem permissão para a operação em geral; é o alvo — ele
        // mesmo — que conflita com a regra de o sistema nunca ficar sem gestor.
        ChangeUserResult.SelfChangeNotAllowed self => TypedResults.Problem(
            detail: self.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Alteração da própria conta não permitida"),

        // Os chamados vão no corpo para a interface listar o que reatribuir, em vez de
        // mandar o gestor procurar.
        ChangeUserResult.HasOpenAssignedTickets blocking => TypedResults.Problem(
            detail: blocking.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Há chamados em aberto sob responsabilidade da pessoa",
            extensions: new Dictionary<string, object?> { ["tickets"] = blocking.Tickets }),

        _ => throw new InvalidOperationException(
            $"Resultado de administração de usuário não tratado: {result.GetType().Name}.")
    };
}
