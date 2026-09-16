using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpsDesk.Infrastructure.Persistence;

/// <summary>
/// Contexto usado só pelas ferramentas de migration, para que <c>dotnet ef</c> não precise
/// subir a API inteira nem resolver o usuário da requisição.
///
/// A connection string vem de <c>OPSDESK_MIGRATIONS_CONNECTION</c> quando definida; o
/// padrão aponta para o Postgres do docker compose. Nada aqui roda em produção — a geração
/// de migration não toca o banco de verdade.
/// </summary>
public class OpsDeskDbContextFactory : IDesignTimeDbContextFactory<OpsDeskDbContext>
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=opsdesk;Username=opsdesk;Password=opsdesk";

    public OpsDeskDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("OPSDESK_MIGRATIONS_CONNECTION") ?? DefaultConnection;

        var options = new DbContextOptionsBuilder<OpsDeskDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OpsDeskDbContext(options);
    }
}
