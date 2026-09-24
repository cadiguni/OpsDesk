using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Tickets;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Filtro por período de abertura e ordenação por última atualização.
///
/// O período é o caso delicado: a interface manda o instante com o deslocamento de São
/// Paulo, e é aí que moram tanto a fronteira do dia quanto a recusa do Npgsql a
/// deslocamento diferente de zero.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TicketListFilterTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    // 23:30 do dia 9 e 00:30 do dia 10, em São Paulo. Em UTC, os dois são dia 10.
    private static readonly DateTimeOffset LateOnTheNinth = new(2026, 9, 10, 2, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EarlyOnTheTenth = new(2026, 9, 10, 3, 30, 0, TimeSpan.Zero);

    private const string MidnightOfTheTenth = "2026-09-10T00:00:00-03:00";

    [Fact]
    public async Task Periodo_com_deslocamento_de_sao_paulo_respeita_a_virada_do_dia_local()
    {
        var world = await SetUpAsync();
        var (ninth, tenth) = await TwoTicketsAroundMidnightAsync(world);

        var from = await ListAsync(world.Manager, $"?createdFrom={Uri.EscapeDataString(MidnightOfTheTenth)}");
        Assert.Equal(tenth, Assert.Single(from.Items).Id);

        var before = await ListAsync(world.Manager, $"?createdBefore={Uri.EscapeDataString(MidnightOfTheTenth)}");
        Assert.Equal(ninth, Assert.Single(before.Items).Id);
    }

    [Fact]
    public async Task Periodo_semiaberto_nao_conta_o_mesmo_chamado_em_dois_periodos_vizinhos()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await SetCreatedAtAsync(ticket, EarlyOnTheTenth);
        var boundary = Uri.EscapeDataString(EarlyOnTheTenth.ToString("O"));

        Assert.Single((await ListAsync(world.Manager, $"?createdFrom={boundary}")).Items);
        Assert.Empty((await ListAsync(world.Manager, $"?createdBefore={boundary}")).Items);
    }

    [Fact]
    public async Task Periodo_nao_amplia_o_que_o_solicitante_ve()
    {
        var world = await SetUpAsync();
        await TwoTicketsAroundMidnightAsync(world);

        var theirs = await OpenTicketAsync(world, client: world.OtherRequester);
        await SetCreatedAtAsync(theirs, EarlyOnTheTenth);

        var response = await world.Requester.GetAsync(
            $"/api/tickets?createdFrom={Uri.EscapeDataString(MidnightOfTheTenth)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadJsonAsync<Application.Common.PagedResult<TicketListItem>>();
        Assert.DoesNotContain(page!.Items, t => t.Id == theirs);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Comentario_leva_o_chamado_ao_topo_dos_atualizados_recentemente()
    {
        var world = await SetUpAsync();
        var older = await OpenTicketAsync(world, title: "Mais antigo");
        await OpenTicketAsync(world, title: "Mais novo");

        var response = await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{older}/comments", new AddCommentRequest("Alguma novidade?"));
        response.EnsureSuccessStatusCode();

        var page = await ListAsync(world.Manager, "?sort=UpdatedAtDescending");

        Assert.Equal(older, page.Items[0].Id);
        Assert.True(page.Items[0].UpdatedAt >= page.Items[1].UpdatedAt);
    }

    private async Task<(Guid Ninth, Guid Tenth)> TwoTicketsAroundMidnightAsync(World world)
    {
        var ninth = await OpenTicketAsync(world, title: "Aberto na noite do dia 9");
        var tenth = await OpenTicketAsync(world, title: "Aberto na madrugada do dia 10");

        await SetCreatedAtAsync(ninth, LateOnTheNinth);
        await SetCreatedAtAsync(tenth, EarlyOnTheTenth);

        return (ninth, tenth);
    }

    private async Task SetCreatedAtAsync(Guid ticket, DateTimeOffset createdAt)
    {
        await using var db = Fixture.CreateContext();

        await db.Tickets.Where(t => t.Id == ticket)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.CreatedAt, createdAt));
    }
}
