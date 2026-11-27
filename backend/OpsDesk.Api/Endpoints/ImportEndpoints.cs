using System.Text.Json;
using OpsDesk.Api.Authorization;
using OpsDesk.Application.Import;

namespace OpsDesk.Api.Endpoints;

/// <summary>Importação de chamados de outra ferramenta (README, seção 13.14). Só gestor.</summary>
public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/admin/import/tickets", Import)
            .RequireAuthorization(AuthorizationPolicies.Manager)
            .DisableAntiforgery()
            .WithTags("Importação")
            .WithSummary("Confere (dryRun=true, o padrão) ou importa chamados de um CSV ou JSON.");

        routes.MapPost("/api/admin/import/spiceworks", ReconcileSpiceworks)
            .RequireAuthorization(AuthorizationPolicies.Manager)
            .DisableAntiforgery()
            .WithTags("Importação")
            .WithSummary("Confere (dryRun=true, o padrão) ou aplica a conciliação dos chamados de e-mail com a exportação JSON do Spiceworks.");

        return routes;
    }

    private static async Task<IResult> Import(
        IFormFile file,
        TicketImportService import,
        CancellationToken cancellationToken,
        ImportFormat format = ImportFormat.OpsDesk,
        bool dryRun = true)
    {
        if (file.Length == 0)
        {
            return TypedResults.Problem(
                detail: "O arquivo está vazio.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Arquivo vazio");
        }

        if (file.Length > TicketImportService.MaxFileBytes)
        {
            return TypedResults.Problem(
                detail: "O arquivo passa de 10 MB. Divida a exportação em partes.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Arquivo grande demais");
        }

        // Sem BOM, o leitor assume UTF-8 — o que o Freshdesk e o Freshservice exportam, e o
        // que o Excel grava em "CSV UTF-8". O "CSV (separado por ponto e vírgula)" antigo do
        // Excel é Windows-1252 e chegaria com acento quebrado; a tela avisa para salvar como UTF-8.
        using var reader = new StreamReader(file.OpenReadStream(), detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var isJson = file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        return TypedResults.Ok(await import.ImportAsync(content, isJson, format, dryRun, cancellationToken));
    }

    private static async Task<IResult> ReconcileSpiceworks(
        IFormFile file,
        SpiceworksReconciliation reconciliation,
        CancellationToken cancellationToken,
        bool dryRun = true)
    {
        if (file.Length == 0)
        {
            return TypedResults.Problem(
                detail: "O arquivo está vazio.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Arquivo vazio");
        }

        if (file.Length > SpiceworksReconciliation.MaxFileBytes)
        {
            return TypedResults.Problem(
                detail: "O arquivo passa de 25 MB.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Arquivo grande demais");
        }

        using var reader = new StreamReader(file.OpenReadStream(), detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            return TypedResults.Ok(await reconciliation.RunAsync(content, dryRun, cancellationToken));
        }
        catch (JsonException ex)
        {
            return TypedResults.Problem(
                detail: $"Não foi possível ler a exportação: {ex.Message}",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Arquivo inválido");
        }
    }
}
