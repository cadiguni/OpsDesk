using Microsoft.EntityFrameworkCore;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Invariante 5 do CLAUDE.md: o código do chamado vem da sequence do PostgreSQL.
/// Estes testes existem porque <c>COUNT(*) + 1</c> passa em qualquer teste sequencial e
/// só falha em produção, sob concorrência.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TicketCodeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Codigo_e_gerado_pelo_banco_no_formato_documentado()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var scenario = await TestData.SeedScenarioAsync(db);

        var codes = await db.Tickets
            .OrderBy(t => t.Code)
            .Select(t => t.Code)
            .ToListAsync();

        Assert.Equal(["OPS-000001", "OPS-000002", "OPS-000003", "OPS-000004"], codes);
        Assert.NotNull(scenario.Own.Code);
    }

    [Fact]
    public async Task Codigo_volta_preenchido_na_propria_insercao()
    {
        // Sem isso a API teria que reconsultar o chamado para poder redirecionar o usuário
        // para a tela de detalhe depois de criar.
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var requester = TestData.NewUser(Domain.Enums.UserRole.Requester);
        var category = TestData.NewCategory();
        db.AddRange(requester, category);
        await db.SaveChangesAsync();

        var ticket = TestData.NewTicket(requester, category);
        db.Add(ticket);
        await db.SaveChangesAsync();

        Assert.Equal("OPS-000001", ticket.Code);
    }

    [Fact]
    public async Task Insercoes_simultaneas_nunca_repetem_codigo()
    {
        await fixture.ResetAsync();

        await using var setup = fixture.CreateContext();
        var requester = TestData.NewUser(Domain.Enums.UserRole.Requester);
        var category = TestData.NewCategory();
        setup.AddRange(requester, category);
        await setup.SaveChangesAsync();

        // Vinte conexões distintas inserindo ao mesmo tempo. Com contador em memória ou
        // MAX(id) + 1 este teste falha; com sequence, não tem como falhar.
        const int concurrentInserts = 20;

        await Task.WhenAll(Enumerable.Range(0, concurrentInserts).Select(async _ =>
        {
            await using var db = fixture.CreateContext();
            db.Add(TestData.NewTicket(requester, category));
            await db.SaveChangesAsync();
        }));

        await using var verify = fixture.CreateContext();
        var codes = await verify.Tickets.Select(t => t.Code).ToListAsync();

        Assert.Equal(concurrentInserts, codes.Count);
        Assert.Equal(concurrentInserts, codes.Distinct().Count());
        Assert.All(codes, code => Assert.Matches(@"^OPS-\d{6}$", code));
    }

    [Fact]
    public async Task Codigo_e_unico_no_banco()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var requester = TestData.NewUser(Domain.Enums.UserRole.Requester);
        var category = TestData.NewCategory();
        db.AddRange(requester, category);
        await db.SaveChangesAsync();

        var first = TestData.NewTicket(requester, category);
        db.Add(first);
        await db.SaveChangesAsync();

        // Forçar o mesmo código na mão precisa bater no índice único.
        var duplicate = TestData.NewTicket(requester, category);
        duplicate.Code = first.Code;

        await using var other = fixture.CreateContext();
        other.Add(duplicate);

        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }
}
