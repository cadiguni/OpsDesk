using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpsDesk.Infrastructure.Persistence;

namespace OpsDesk.Api.Maintenance;

/// <summary>
/// Readiness: a aplicação consegue atender, e não apenas responder.
///
/// O <c>/health</c> pergunta se o processo está vivo e se o banco responde. Isso basta
/// para o orquestrador decidir se reinicia o contêiner, e não basta para decidir se manda
/// tráfego: uma instalação em que ninguém rodou <c>--setup</c> tem banco respondendo,
/// <c>/health</c> em 200 — e recusa todo chamado novo com 500, porque não há política de
/// SLA para calcular prazo.
///
/// Separar os dois põe o problema onde ele custa menos: no deploy, e não no primeiro
/// chamado que alguém tentar abrir.
/// </summary>
internal static class ReadinessChecks
{
    /// <summary>
    /// Marca os testes que só o <c>/ready</c> executa. O <c>/health</c> os exclui de
    /// propósito: schema desatualizado é motivo para não receber tráfego, nunca para o
    /// orquestrador matar e recriar o contêiner — reiniciar não aplica migration, e o
    /// resultado seria um laço de reinício sem diagnóstico.
    /// </summary>
    public const string Tag = "ready";

    public static IHealthChecksBuilder AddOpsDeskReadiness(this IHealthChecksBuilder builder) => builder
        .AddCheck<SchemaUpToDateCheck>("schema", tags: [Tag])
        .AddCheck<ReferenceDataCheck>("reference-data", tags: [Tag]);
}

/// <summary>
/// Corpo do <c>/ready</c>.
///
/// O escritor padrão devolve a palavra <c>Unhealthy</c> e nada mais, o que transforma um
/// diagnóstico pronto — "rode --seed", "há duas migrations pendentes" — em "alguma coisa
/// está errada". Como o motivo deste endpoint existir é justamente dizer o que fazer,
/// escrevemos a descrição de cada verificação.
///
/// É anônimo, como o <c>/health</c>, porque o orquestrador consulta sem credencial. Por
/// isso o texto fala de schema e de dados de referência, e nunca de connection string,
/// caminho de arquivo ou qualquer outro detalhe de infraestrutura.
/// </summary>
internal static class ReadinessResponse
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description
                })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}

/// <summary>O schema do banco está na versão desta aplicação.</summary>
internal class SchemaUpToDateCheck(OpsDeskDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        return pending.Count == 0
            ? HealthCheckResult.Healthy("Schema na versão da aplicação.")
            : HealthCheckResult.Unhealthy(
                $"{pending.Count} migration(s) pendente(s): {string.Join(", ", pending)}. " +
                "Rode `--migrate` antes de liberar tráfego.");
    }
}

/// <summary>
/// Os dados de referência existem.
///
/// Categoria e política de SLA são pré-requisito de funcionamento, não dado de exemplo:
/// sem política não há prazo, e abrir chamado falha alto de propósito. Feriado fica de
/// fora desta verificação porque a ausência dele não quebra nada — só faz o cálculo tratar
/// feriado como dia útil, que é um erro de resultado, não de disponibilidade.
/// </summary>
internal class ReferenceDataCheck(OpsDeskDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();

        if (!await db.SlaPolicies.AnyAsync(p => p.IsActive, cancellationToken))
        {
            missing.Add("nenhuma política de SLA ativa");
        }

        if (!await db.Categories.AnyAsync(c => c.IsActive, cancellationToken))
        {
            missing.Add("nenhuma categoria ativa");
        }

        return missing.Count == 0
            ? HealthCheckResult.Healthy("Dados de referência presentes.")
            : HealthCheckResult.Unhealthy(
                $"Instalação incompleta: {string.Join(" e ", missing)}. Rode `--seed`.");
    }
}
