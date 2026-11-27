using System.Net;
using System.Net.Http.Json;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Mudança de status em lote (<c>POST /api/tickets/bulk/status</c>): o mesmo caminho da
/// mudança individual para cada chamado, com resultado por chamado em vez de tudo ou nada.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BulkStatusTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    [Fact]
    public async Task Fecha_os_que_podem_e_conta_por_que_os_outros_nao_mudaram()
    {
        var world = await SetUpAsync();

        var open = await OpenTicketAsync(world, "Impressora");
        var inProgress = await OpenTicketAsync(world, "VPN");
        var cancelled = await OpenTicketAsync(world, "Monitor");
        await ChangeStatusAsync(world.Technician, inProgress, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, cancelled, TicketStatus.Cancelled);

        var results = await BulkAsync(world.Technician, [open, inProgress, cancelled], TicketStatus.Closed);

        Assert.Equal(BulkStatusOutcome.Changed, results.Single(r => r.TicketId == open).Outcome);
        Assert.Equal(BulkStatusOutcome.Changed, results.Single(r => r.TicketId == inProgress).Outcome);

        var refused = results.Single(r => r.TicketId == cancelled);
        Assert.Equal(BulkStatusOutcome.TransitionRejected, refused.Outcome);
        Assert.Equal(TicketStatus.Cancelled, refused.Status);
        Assert.Equal((await GetAsync(world.Technician, cancelled)).Code, refused.Code);

        Assert.Equal(TicketStatus.Closed, (await GetAsync(world.Technician, open)).Status);
        Assert.Equal(TicketStatus.Closed, (await GetAsync(world.Technician, inProgress)).Status);
        Assert.Equal(TicketStatus.Cancelled, (await GetAsync(world.Technician, cancelled)).Status);

        // A trilha é a da mudança individual: um registro de fechamento por chamado.
        Assert.Single(await HistoryAsync(world.Technician, open), h => h.Action == TicketHistoryAction.Closed);
        Assert.Single(await HistoryAsync(world.Technician, inProgress), h => h.Action == TicketHistoryAction.Closed);
    }

    [Fact]
    public async Task Chamado_inexistente_volta_como_nao_encontrado_sem_impedir_os_outros()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        var unknown = Guid.NewGuid();

        var results = await BulkAsync(world.Manager, [ticket, unknown, ticket], TicketStatus.Triage);

        Assert.Equal(2, results.Count);
        Assert.Equal(BulkStatusOutcome.Changed, results.Single(r => r.TicketId == ticket).Outcome);

        var missing = results.Single(r => r.TicketId == unknown);
        Assert.Equal(BulkStatusOutcome.NotFound, missing.Outcome);
        Assert.Null(missing.Code);
    }

    [Fact]
    public async Task Solicitante_nao_usa_a_mudanca_em_lote_nem_no_proprio_chamado()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await world.Requester.PostAsJsonAsync(
            "/api/tickets/bulk/status", new BulkChangeStatusRequest([ticket], TicketStatus.Cancelled));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TicketStatus.Open, (await GetAsync(world.Requester, ticket)).Status);
    }

    [Fact]
    public async Task Lote_vazio_ou_maior_que_o_teto_e_recusado()
    {
        var world = await SetUpAsync();

        var empty = await world.Technician.PostAsJsonAsync(
            "/api/tickets/bulk/status", new BulkChangeStatusRequest([], TicketStatus.Closed));

        var tooMany = await world.Technician.PostAsJsonAsync(
            "/api/tickets/bulk/status",
            new BulkChangeStatusRequest([.. Enumerable.Range(0, BulkChangeStatusRequest.MaxTickets + 1).Select(_ => Guid.NewGuid())], TicketStatus.Closed));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    private static async Task<List<BulkStatusItem>> BulkAsync(HttpClient client, Guid[] ids, TicketStatus status)
    {
        var response = await client.PostAsJsonAsync("/api/tickets/bulk/status", new BulkChangeStatusRequest(ids, status));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<List<BulkStatusItem>>())!;
    }
}
