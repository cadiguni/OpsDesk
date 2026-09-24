using OpsDesk.Api.Authorization;
using OpsDesk.Api.Validation;
using OpsDesk.Application.Categories;
using OpsDesk.Application.Common;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// Administração de categorias (README, seção 13.10). Só gestor.
///
/// Separada de <c>/api/categories</c> pelo mesmo motivo da de usuários: aquela serve os
/// seletores e devolve só as ativas, para qualquer pessoa autenticada; esta lista as
/// desativadas também, com contagem de chamados, e só para quem administra.
/// </summary>
public static class CategoryAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapCategoryAdministrationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/admin/categories")
            .RequireAuthorization(AuthorizationPolicies.Manager)
            .WithTags("Administração de categorias");

        group.MapGet("/", List)
            .WithSummary("Categorias, ativas e desativadas, com filtros e paginação.");

        group.MapPost("/", Create)
            .ValidatingBody<SaveCategoryRequest>()
            .WithSummary("Cria uma categoria.");

        group.MapPut("/{id:guid}", Update)
            .ValidatingBody<SaveCategoryRequest>()
            .WithSummary("Renomeia e troca a descrição.");

        group.MapPost("/{id:guid}/activation", ChangeActivation)
            .WithSummary("Desativa ou reativa. Desativada sai dos seletores; os chamados continuam.");

        return routes;
    }

    private static async Task<IResult> List(
        CategoryAdministrationService categories,
        CancellationToken cancellationToken,
        string? search = null,
        bool? isActive = null,
        int? page = null,
        int? pageSize = null)
    {
        var filter = new ManagedCategoryFilter(search, isActive)
        {
            Page = new PageRequest(page, pageSize)
        };

        return TypedResults.Ok(await categories.ListAsync(filter, cancellationToken));
    }

    private static async Task<IResult> Create(
        SaveCategoryRequest request,
        CategoryAdministrationService categories,
        CancellationToken cancellationToken)
    {
        var result = await categories.CreateAsync(request, cancellationToken);

        return result is SaveCategoryResult.Saved saved
            ? TypedResults.Created($"/api/admin/categories/{saved.Category.Id}", saved.Category)
            : ToResult(result);
    }

    private static async Task<IResult> Update(
        Guid id,
        SaveCategoryRequest request,
        CategoryAdministrationService categories,
        CancellationToken cancellationToken) =>
        ToResult(await categories.UpdateAsync(id, request, cancellationToken));

    private static async Task<IResult> ChangeActivation(
        Guid id,
        ChangeCategoryActivationRequest request,
        CategoryAdministrationService categories,
        CancellationToken cancellationToken) =>
        ToResult(await categories.ChangeActivationAsync(id, request, cancellationToken));

    private static IResult ToResult(SaveCategoryResult result) => result switch
    {
        SaveCategoryResult.Saved saved => TypedResults.Ok(saved.Category),

        SaveCategoryResult.CategoryNotFound => TypedResults.NotFound(),

        SaveCategoryResult.NameTaken taken => TypedResults.Problem(
            detail: taken.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Nome de categoria em uso"),

        SaveCategoryResult.LastActiveCategory last => TypedResults.Problem(
            detail: last.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Última categoria ativa"),

        _ => throw new InvalidOperationException(
            $"Resultado de administração de categoria não tratado: {result.GetType().Name}.")
    };
}
