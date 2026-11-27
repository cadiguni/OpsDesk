using OpsDesk.Application.Fun;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// A arquibancada: tabela do Brasileirão e jogos de um time, para a área de descontração
/// que não aparece em menu nenhum.
///
/// Fica fora do OpenAPI de propósito (<c>ExcludeFromDescription</c>), pelo mesmo motivo de
/// não ter link: é brincadeira, não contrato. Mas não fica fora da segurança — exige sessão
/// como qualquer rota, e a trava de senha provisória vale aqui como em todo o resto.
/// </summary>
public static class FunEndpoints
{
    public static IEndpointRouteBuilder MapFunEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/arquibancada")
            .RequireAuthorization()
            .ExcludeFromDescription();

        group.MapGet("/standings", Standings);
        group.MapGet("/teams/{teamId:int}/matches", TeamMatches);

        return routes;
    }

    private static Task<IResult> Standings(IFootballFeed feed, CancellationToken cancellationToken) =>
        Guarded(feed, async () => TypedResults.Ok(await feed.GetStandingsAsync(cancellationToken)));

    private static Task<IResult> TeamMatches(int teamId, IFootballFeed feed, CancellationToken cancellationToken) =>
        Guarded(feed, async () => await feed.GetTeamMatchesAsync(teamId, cancellationToken) is { } matches
            ? TypedResults.Ok(matches)
            : TypedResults.NotFound());

    private static async Task<IResult> Guarded(IFootballFeed feed, Func<Task<IResult>> action)
    {
        if (!feed.IsConfigured)
        {
            return TypedResults.Problem(
                detail: "Defina Football__ApiKey com um token da football-data.org para ver a tabela.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Arquibancada sem dados configurados");
        }

        try
        {
            return await action();
        }
        catch (FootballFeedUnavailableException exception)
        {
            return TypedResults.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway,
                title: "Placar fora do ar");
        }
    }
}
