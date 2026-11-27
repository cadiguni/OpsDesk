using OpsDesk.Api.Authorization;
using OpsDesk.Api.Validation;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Common;
using OpsDesk.Application.Users;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// Administração de usuários (README, seção 13.9). Só administrador.
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

        group.MapPost("/", Create)
            .ValidatingBody<CreateUserRequest>()
            .WithSummary("Cadastra uma pessoa: convite por e-mail, ou senha provisória mostrada uma vez.");

        group.MapPost("/{id:guid}/password-reset", ResetPassword)
            .WithSummary("Redefine a senha de outra pessoa. As sessões abertas dela caem.");

        group.MapGet("/{id:guid}/history", History)
            .WithSummary("Trilha de auditoria da pessoa: criação, perfil, ativação e senha.");

        group.MapPost("/{id:guid}/role", ChangeRole)
            .ValidatingBody<ChangeRoleRequest>()
            .WithSummary("Troca o perfil. Rebaixar derruba as sessões da pessoa.");

        group.MapPost("/{id:guid}/name", ChangeName)
            .ValidatingBody<ChangeNameRequest>()
            .WithSummary("Troca o nome exibido. Vale também para a própria conta.");

        group.MapPost("/{id:guid}/job-title", ChangeJobTitle)
            .ValidatingBody<ChangeJobTitleRequest>()
            .WithSummary("Troca o cargo, só informativo. Vale também para a própria conta.");

        group.MapPost("/{id:guid}/activation", ChangeActivation)
            .WithSummary("Desativa ou reativa a conta. Desativar derruba as sessões da pessoa.");

        group.MapDelete("/{id:guid}", Delete)
            .WithSummary("Exclui conta desativada que não deixou rastro. Com histórico, a conta só fica desativada.");

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

    private static async Task<IResult> Create(
        CreateUserRequest request,
        UserAdministrationService users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var result = await users.CreateAsync(request, currentUser.Viewer, cancellationToken);

        return result is ChangeUserResult.AccessIssued issued
            ? TypedResults.Created($"/api/admin/users/{issued.Access.User.Id}", issued.Access)
            : ToResult(result);
    }

    private static async Task<IResult> ResetPassword(
        Guid id, UserAdministrationService users, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        ToResult(await users.ResetPasswordAsync(id, currentUser.Viewer, cancellationToken));

    private static async Task<IResult> History(
        Guid id, UserAdministrationService users, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        await users.HistoryAsync(id, currentUser.Viewer, cancellationToken) is { } history
            ? TypedResults.Ok(history)
            : TypedResults.NotFound();

    private static async Task<IResult> ChangeRole(
        Guid id,
        ChangeRoleRequest request,
        UserAdministrationService users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        ToResult(await users.ChangeRoleAsync(id, request, currentUser.Viewer, cancellationToken));

    private static async Task<IResult> ChangeName(
        Guid id,
        ChangeNameRequest request,
        UserAdministrationService users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        ToResult(await users.ChangeNameAsync(id, request, currentUser.Viewer, cancellationToken));

    private static async Task<IResult> ChangeJobTitle(
        Guid id,
        ChangeJobTitleRequest request,
        UserAdministrationService users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        ToResult(await users.ChangeJobTitleAsync(id, request, currentUser.Viewer, cancellationToken));

    private static async Task<IResult> ChangeActivation(
        Guid id,
        ChangeActivationRequest request,
        UserAdministrationService users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        ToResult(await users.ChangeActivationAsync(id, request, currentUser.Viewer, cancellationToken));

    private static async Task<IResult> Delete(
        Guid id, UserAdministrationService users, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        ToResult(await users.DeleteAsync(id, currentUser.Viewer, cancellationToken));

    private static IResult ToResult(ChangeUserResult result) => result switch
    {
        ChangeUserResult.Deleted => TypedResults.NoContent(),

        ChangeUserResult.Changed changed => TypedResults.Ok(changed.User),

        ChangeUserResult.AccessIssued issued => TypedResults.Ok(issued.Access),

        ChangeUserResult.EmailAlreadyUsed used => TypedResults.Problem(
            detail: used.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "E-mail já cadastrado"),

        ChangeUserResult.UserNotFound => TypedResults.NotFound(),

        ChangeUserResult.JobTitleUnavailable unavailable => TypedResults.Problem(
            detail: unavailable.Message,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Cargo indisponível"),

        ChangeUserResult.NotAllowed => TypedResults.Problem(
            detail: "Apenas administradores ativos administram usuários.",
            statusCode: StatusCodes.Status403Forbidden,
            title: "Administração não permitida"),

        // 409 e não 403: o administrador tem permissão para a operação em geral; é o alvo —
        // ele mesmo — que conflita com a regra de o sistema nunca ficar sem administrador.
        ChangeUserResult.SelfChangeNotAllowed self => TypedResults.Problem(
            detail: self.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Alteração da própria conta não permitida"),

        // Os chamados vão no corpo para a interface listar o que reatribuir, em vez de
        // mandar o administrador procurar.
        ChangeUserResult.HasOpenAssignedTickets blocking => TypedResults.Problem(
            detail: blocking.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Há chamados em aberto sob responsabilidade da pessoa",
            extensions: new Dictionary<string, object?> { ["tickets"] = blocking.Tickets }),

        ChangeUserResult.StillActive active => TypedResults.Problem(
            detail: active.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Conta ainda ativa"),

        ChangeUserResult.HasActivity activity => TypedResults.Problem(
            detail: activity.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "A conta tem histórico no sistema"),

        _ => throw new InvalidOperationException(
            $"Resultado de administração de usuário não tratado: {result.GetType().Name}.")
    };
}
