using OpsDesk.Api.Authorization;
using OpsDesk.Api.Validation;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Holidays;

namespace OpsDesk.Api.Endpoints;

/// <summary>Feriados do expediente (README, seção 13.11). Só gestor.</summary>
public static class HolidayEndpoints
{
    public static IEndpointRouteBuilder MapHolidayEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/admin/holidays")
            .RequireAuthorization(AuthorizationPolicies.Manager)
            .WithTags("Feriados");

        group.MapGet("/", List)
            .WithSummary("Feriados de um ano, ligados e desligados.");

        group.MapPost("/", Add)
            .ValidatingBody<AddHolidayRequest>()
            .WithSummary("Cadastra um feriado municipal, estadual ou recesso.");

        group.MapPost("/{id:guid}/activation", ChangeActivation)
            .WithSummary("Liga ou desliga um feriado.");

        group.MapDelete("/{id:guid}", Delete)
            .WithSummary("Apaga um feriado cadastrado pelo administrador. Nacional se desliga, não se apaga.");

        return routes;
    }

    private static async Task<IResult> List(
        HolidayAdministrationService holidays, IClock clock, CancellationToken cancellationToken, int? year = null) =>
        TypedResults.Ok(await holidays.ListAsync(year ?? clock.UtcNow.Year, cancellationToken));

    private static async Task<IResult> Add(
        AddHolidayRequest request, HolidayAdministrationService holidays, CancellationToken cancellationToken)
    {
        var result = await holidays.AddAsync(request, cancellationToken);

        return result is HolidayChangeResult.Changed changed
            ? TypedResults.Created($"/api/admin/holidays/{changed.Holiday.Id}", changed.Holiday)
            : ToResult(result);
    }

    private static async Task<IResult> ChangeActivation(
        Guid id,
        ChangeHolidayActivationRequest request,
        HolidayAdministrationService holidays,
        CancellationToken cancellationToken) =>
        ToResult(await holidays.ChangeActivationAsync(id, request, cancellationToken));

    private static async Task<IResult> Delete(
        Guid id, HolidayAdministrationService holidays, CancellationToken cancellationToken)
    {
        var result = await holidays.DeleteAsync(id, cancellationToken);

        return result is HolidayChangeResult.Changed ? TypedResults.NoContent() : ToResult(result);
    }

    private static IResult ToResult(HolidayChangeResult result) => result switch
    {
        HolidayChangeResult.Changed changed => TypedResults.Ok(changed.Holiday),

        HolidayChangeResult.NotFound => TypedResults.NotFound(),

        HolidayChangeResult.DateTaken taken => TypedResults.Problem(
            detail: taken.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Data já tem feriado"),

        HolidayChangeResult.NationalCannotBeDeleted national => TypedResults.Problem(
            detail: national.Message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Feriado nacional"),

        _ => throw new InvalidOperationException($"Resultado de feriado não tratado: {result.GetType().Name}.")
    };
}
