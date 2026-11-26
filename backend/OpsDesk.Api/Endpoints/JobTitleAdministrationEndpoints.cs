using OpsDesk.Api.Authorization;
using OpsDesk.Api.Validation;
using OpsDesk.Application.Common;
using OpsDesk.Application.JobTitles;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// Catálogo de cargos. Só administrador.
///
/// Cargo é informativo e só aparece na administração de usuários, então até o seletor
/// (<c>/options</c>) fica atrás da mesma política.
/// </summary>
public static class JobTitleAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapJobTitleAdministrationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/admin/job-titles")
            .RequireAuthorization(AuthorizationPolicies.Manager)
            .WithTags("Administração de cargos");

        group.MapGet("/", List)
            .WithSummary("Cargos, ativos e desativados, com filtros e paginação.");

        group.MapGet("/options", Options)
            .WithSummary("Cargos ativos, para o seletor do cadastro de usuário.");

        group.MapPost("/", Create)
            .ValidatingBody<SaveJobTitleRequest>()
            .WithSummary("Cria um cargo.");

        group.MapPut("/{id:guid}", Update)
            .ValidatingBody<SaveJobTitleRequest>()
            .WithSummary("Renomeia o cargo.");

        group.MapPost("/{id:guid}/activation", ChangeActivation)
            .WithSummary("Desativa ou reativa. Desativado sai do seletor; quem o tem continua com ele.");

        return routes;
    }

    private static async Task<IResult> List(
        JobTitleAdministrationService jobTitles,
        CancellationToken cancellationToken,
        string? search = null,
        bool? isActive = null,
        int? page = null,
        int? pageSize = null)
    {
        var filter = new ManagedJobTitleFilter(search, isActive) { Page = new PageRequest(page, pageSize) };

        return TypedResults.Ok(await jobTitles.ListAsync(filter, cancellationToken));
    }

    private static async Task<IResult> Options(
        JobTitleAdministrationService jobTitles, CancellationToken cancellationToken) =>
        TypedResults.Ok(await jobTitles.OptionsAsync(cancellationToken));

    private static async Task<IResult> Create(
        SaveJobTitleRequest request, JobTitleAdministrationService jobTitles, CancellationToken cancellationToken)
    {
        var result = await jobTitles.CreateAsync(request, cancellationToken);

        return result is SaveJobTitleResult.Saved saved
            ? TypedResults.Created($"/api/admin/job-titles/{saved.JobTitle.Id}", saved.JobTitle)
            : ToResult(result);
    }

    private static async Task<IResult> Update(
        Guid id, SaveJobTitleRequest request, JobTitleAdministrationService jobTitles, CancellationToken cancellationToken) =>
        ToResult(await jobTitles.UpdateAsync(id, request, cancellationToken));

    private static async Task<IResult> ChangeActivation(
        Guid id,
        ChangeJobTitleActivationRequest request,
        JobTitleAdministrationService jobTitles,
        CancellationToken cancellationToken) =>
        ToResult(await jobTitles.ChangeActivationAsync(id, request, cancellationToken));

    private static IResult ToResult(SaveJobTitleResult result) => result switch
    {
        SaveJobTitleResult.Saved saved => TypedResults.Ok(saved.JobTitle),

        SaveJobTitleResult.JobTitleNotFound => TypedResults.NotFound(),

        SaveJobTitleResult.NameTaken taken => TypedResults.Problem(
            detail: taken.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Nome de cargo em uso"),

        _ => throw new InvalidOperationException(
            $"Resultado de administração de cargo não tratado: {result.GetType().Name}.")
    };
}
