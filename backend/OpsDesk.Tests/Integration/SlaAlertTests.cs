using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Application.Common;
using OpsDesk.Application.Notifications;
using OpsDesk.Application.Sla;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Alertas de SLA gerados pela verificação periódica.
///
/// A API usa o relógio do sistema, então a passagem do tempo é simulada nos prazos: um
/// minuto à frente é "perto de vencer" em qualquer horário do teste, nove dias à frente
/// nunca é, e cinco minutos atrás é "acabou de vencer".
/// </summary>
[Collection(PostgresCollection.Name)]
public class SlaAlertTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    [Fact]
    public async Task Prazo_perto_de_vencer_avisa_o_responsavel()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);
        await SetDeadlinesAsync(ticket, response: TimeSpan.FromDays(9), resolution: TimeSpan.FromMinutes(1));

        Assert.Equal(1, await RunAsync());

        var alert = Assert.Single(await SlaNotificationsAsync(world.Technician));
        Assert.Equal(NotificationKind.SlaDueSoon, alert.Kind);
        Assert.Equal(nameof(SlaDeadline.Resolution), alert.Detail);
        Assert.Null(alert.ActorName);

        Assert.Empty(await SlaNotificationsAsync(world.Manager));
        Assert.Empty(await SlaNotificationsAsync(world.Requester));
    }

    [Fact]
    public async Task Chamado_sem_responsavel_avisa_os_gestores()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await SetDeadlinesAsync(ticket, response: TimeSpan.FromMinutes(-5), resolution: TimeSpan.FromDays(9));

        await RunAsync();

        var alert = Assert.Single(await SlaNotificationsAsync(world.Manager));
        Assert.Equal(NotificationKind.SlaOverdue, alert.Kind);
        Assert.Equal(nameof(SlaDeadline.Response), alert.Detail);
        Assert.Empty(await SlaNotificationsAsync(world.Technician));
    }

    [Fact]
    public async Task Mesmo_alerta_nao_se_repete_nas_rodadas_seguintes()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);
        await SetDeadlinesAsync(ticket, response: TimeSpan.FromDays(9), resolution: TimeSpan.FromMinutes(1));

        await RunAsync();
        Assert.Equal(0, await RunAsync());

        Assert.Single(await SlaNotificationsAsync(world.Technician));
    }

    [Fact]
    public async Task Prazo_que_se_move_pode_alertar_de_novo()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);
        await SetDeadlinesAsync(ticket, response: TimeSpan.FromDays(9), resolution: TimeSpan.FromMinutes(1));
        await RunAsync();

        // Pausa encerrada, reabertura ou reclassificação: prazo novo, alerta novo.
        await SetDeadlinesAsync(ticket, response: TimeSpan.FromDays(9), resolution: TimeSpan.FromMinutes(2));
        Assert.Equal(1, await RunAsync());

        Assert.Equal(2, (await SlaNotificationsAsync(world.Technician)).Count);
    }

    [Fact]
    public async Task Prazo_que_nao_corre_nao_alerta()
    {
        var world = await SetUpAsync();

        var answered = await OpenTicketAsync(world, title: "Respondido");
        await CommentAsync(world.Technician, answered, "Já estou olhando.");

        var waiting = await OpenTicketAsync(world, title: "Aguardando solicitante");
        await ChangeStatusAsync(world.Technician, waiting, TicketStatus.InProgress);
        await CommentAsync(world.Technician, waiting, "Qual é o erro exato?");
        await ChangeStatusAsync(world.Technician, waiting, TicketStatus.WaitingOnRequester);

        var cancelled = await OpenTicketAsync(world, title: "Cancelado");
        await ChangeStatusAsync(world.Requester, cancelled, TicketStatus.Cancelled);

        foreach (var ticket in new[] { answered, waiting, cancelled })
        {
            // Resposta já dada nos dois primeiros; resolução pausada no segundo; tudo
            // encerrado no cancelado. A resolução do primeiro corre, então fica longe.
            var resolution = ticket == answered ? TimeSpan.FromDays(9) : TimeSpan.FromMinutes(1);
            await SetDeadlinesAsync(ticket, response: TimeSpan.FromMinutes(1), resolution: resolution);
        }

        Assert.Equal(0, await RunAsync());
    }

    [Fact]
    public async Task Vencimento_antigo_nao_gera_alerta()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await SetDeadlinesAsync(ticket, response: TimeSpan.FromDays(-3), resolution: TimeSpan.FromDays(-2));

        Assert.Equal(0, await RunAsync());
    }

    [Fact]
    public async Task Responsavel_desativado_passa_o_alerta_aos_gestores()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);

        await using (var db = Fixture.CreateContext())
        {
            await db.Users.Where(u => u.Id == world.TechnicianId)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.IsActive, false));
        }

        await SetDeadlinesAsync(ticket, response: TimeSpan.FromDays(9), resolution: TimeSpan.FromMinutes(1));

        await RunAsync();

        Assert.Single(await SlaNotificationsAsync(world.Manager));
    }

    // ----- Atalhos -----

    private async Task SetDeadlinesAsync(Guid ticket, TimeSpan response, TimeSpan resolution)
    {
        var now = DateTimeOffset.UtcNow;

        await using var db = Fixture.CreateContext();

        await db.Tickets.Where(t => t.Id == ticket).ExecuteUpdateAsync(set => set
            .SetProperty(t => t.SlaResponseDueAt, now.Add(response))
            .SetProperty(t => t.SlaResolutionDueAt, now.Add(resolution)));
    }

    private async Task<int> RunAsync()
    {
        using var scope = Fixture.Api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<SlaAlertService>().RunAsync();
    }

    private static async Task CommentAsync(HttpClient client, Guid ticket, string content)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new Application.Tickets.AddCommentRequest(content));

        response.EnsureSuccessStatusCode();
    }

    private static async Task<List<NotificationItem>> SlaNotificationsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/notifications?pageSize=100");

        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadJsonAsync<PagedResult<NotificationItem>>();

        return page!.Items
            .Where(n => n.Kind is NotificationKind.SlaDueSoon or NotificationKind.SlaOverdue)
            .ToList();
    }
}
