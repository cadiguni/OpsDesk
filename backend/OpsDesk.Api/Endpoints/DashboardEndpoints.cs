using OpsDesk.Api.Authorization;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Dashboard;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// Dashboard do gestor (README, seção 13.7).
///
/// A política de rota barra quem não é gestor. O que decide o conteúdo dos números
/// continua sendo o filtro de visibilidade dentro das consultas agregadas — a política
/// controla o acesso à tela, não o escopo do dado.
/// </summary>
public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/dashboard", Summary)
            .RequireAuthorization(AuthorizationPolicies.Manager)
            .WithTags("Dashboard")
            .WithSummary("Indicadores da operação.");

        return routes;
    }

    private static async Task<IResult> Summary(
        DashboardService dashboard,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await dashboard.GetSummaryAsync(currentUser.Viewer, cancellationToken));
}
