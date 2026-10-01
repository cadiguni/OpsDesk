using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Common;
using OpsDesk.Application.Tickets;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Busca da lista de chamados: código, título e descrição, com índice de trigramas.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TicketSearchTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    [Fact]
    public async Task Encontra_pelo_texto_da_descricao()
    {
        var world = await SetUpAsync();
        var ticket = await OpenAsync(world, "Problema no notebook", "A tela pisca quando conecto o projetor da sala 3.");
        await OpenAsync(world, "Outro problema", "Nada a ver com isso.");

        var found = await SearchAsync(world.Manager, "projetor");

        Assert.Equal(ticket, Assert.Single(found.Items).Id);
    }

    [Fact]
    public async Task Porcentagem_e_sublinhado_sao_texto_e_nao_curinga()
    {
        var world = await SetUpAsync();
        var percent = await OpenAsync(world, "Desconto de 50% não aplicado", "O sistema de vendas ignora o cupom.");
        await OpenAsync(world, "Cobrança de 500 reais", "Valor errado na fatura.");
        var underscore = await OpenAsync(world, "Pasta rh_folha sumiu", "A pasta compartilhada não aparece.");
        await OpenAsync(world, "Pasta rhXfolha", "Outra pasta.");

        Assert.Equal(percent, Assert.Single((await SearchAsync(world.Manager, "50%")).Items).Id);
        Assert.Equal(underscore, Assert.Single((await SearchAsync(world.Manager, "rh_folha")).Items).Id);
    }

    [Fact]
    public async Task Solicitante_nao_encontra_chamado_alheio_pela_descricao()
    {
        var world = await SetUpAsync();
        await OpenAsync(world, "Chamado da outra pessoa", "Senha do cofre na descrição.", world.OtherRequester);

        Assert.Empty((await SearchAsync(world.Requester, "cofre")).Items);
    }

    [Fact]
    public async Task Busca_usa_os_indices_de_trigramas()
    {
        await SetUpAsync();
        await using var db = Fixture.CreateContext();

        // A forma da consulta que o EF gera...
        var sql = db.Tickets.ApplyFilter(new TicketFilter(Search: "impressora"), DateTimeOffset.UtcNow).ToQueryString();
        Assert.Contains("lower(t.description) LIKE", sql);
        Assert.Contains("lower(t.title) LIKE", sql);

        // ...é a que os índices aceleram. Sem varredura sequencial disponível, o planejador
        // só tem o índice para usar; se a expressão não casasse, ele não apareceria no plano.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");

        var plan = await db.Database
            .SqlQueryRaw<string>("EXPLAIN SELECT id FROM tickets WHERE lower(description) LIKE '%impressora%' OR lower(title) LIKE '%impressora%'")
            .ToListAsync();

        Assert.Contains(plan, line => line.Contains("ix_tickets_description_trgm"));
        Assert.Contains(plan, line => line.Contains("ix_tickets_title_trgm"));
    }

    [Fact]
    public async Task Equipe_ve_categoria_desativada_no_filtro_e_solicitante_nao()
    {
        var world = await SetUpAsync();

        await using (var db = Fixture.CreateContext())
        {
            await db.Categories.Where(c => c.Id == world.CategoryId)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.IsActive, false));
        }

        var forStaff = await world.Technician.GetFromJsonAsync<List<CategoryOption>>("/api/categories?includeInactive=true", TestJson.Options);
        var forRequester = await world.Requester.GetFromJsonAsync<List<CategoryOption>>("/api/categories?includeInactive=true", TestJson.Options);

        Assert.Contains(forStaff!, c => c.Id == world.CategoryId && !c.IsActive);
        Assert.DoesNotContain(forRequester!, c => c.Id == world.CategoryId);
    }

    private static async Task<Guid> OpenAsync(World world, string title, string description, HttpClient? client = null)
    {
        var response = await (client ?? world.Requester).PostAsJsonAsync(
            "/api/tickets", new CreateTicketRequest(title, description, world.CategoryId, Domain.Enums.TicketPriority.Medium));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<TicketDetail>())!.Id;
    }

    private static async Task<PagedResult<TicketListItem>> SearchAsync(HttpClient client, string term) =>
        (await client.GetFromJsonAsync<PagedResult<TicketListItem>>(
            $"/api/tickets?search={Uri.EscapeDataString(term)}", TestJson.Options))!;
}
