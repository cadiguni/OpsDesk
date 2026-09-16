using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Enums;
using OpsDesk.Infrastructure.Persistence;
using OpsDesk.Infrastructure.Persistence.Interceptors;
using Testcontainers.PostgreSql;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// PostgreSQL efêmero para os testes de integração (decisão 4.8 de docs/arquitetura.md).
///
/// O provider InMemory do EF Core não serve aqui: não tem sequence, não tem
/// <c>timestamptz</c> e não reproduz o comportamento transacional que o projeto usa.
/// Teste que passa nele e falha no Postgres é pior do que não ter teste.
///
/// O schema é criado por migration, igual a qualquer outro ambiente, e não por
/// <c>EnsureCreated</c> — se a migration estiver quebrada, é aqui que aparece.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("opsdesk")
        .WithUsername("opsdesk")
        .WithPassword("opsdesk")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Contexto com os mesmos interceptors da API. Rodar sem eles provaria só que o EF
    /// funciona, e o que precisa de prova é o histórico automático.
    /// </summary>
    public OpsDeskDbContext CreateContext(Guid? currentUserId = null, DateTimeOffset? now = null)
    {
        var clock = new FixedClock(now ?? DateTimeOffset.UtcNow);
        var currentUser = new StubCurrentUser(currentUserId);

        var options = new DbContextOptionsBuilder<OpsDeskDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new TimestampInterceptor(clock),
                new TicketHistoryInterceptor(currentUser, clock))
            .Options;

        return new OpsDeskDbContext(options);
    }

    /// <summary>
    /// Limpa as tabelas entre testes. A sequence do código do chamado é reiniciada à mão
    /// porque <c>TRUNCATE</c> não mexe em sequence que não pertence a uma coluna.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();

        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE ticket_history, ticket_comments, tickets,
                           refresh_tokens, users, categories, sla_policies, holidays
            RESTART IDENTITY CASCADE;
            ALTER SEQUENCE ticket_code_seq RESTART WITH 1;
            """);
    }
}

public class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

public class StubCurrentUser(Guid? userId, UserRole role = UserRole.Manager) : ICurrentUser
{
    public Guid? UserId => userId;

    public bool IsAuthenticated => userId is not null;

    public TicketViewer Viewer => userId is { } id
        ? new TicketViewer(id, role)
        : throw new InvalidOperationException("Não há usuário autenticado.");
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
