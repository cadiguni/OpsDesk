using System.Net;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Dashboard;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Indicadores do período: cumprimento de SLA, reabertura e a comparação com o período
/// anterior. Os chamados têm datas fixadas no banco, para que cada um caia num caso
/// conhecido — cumpriu, não cumpriu, ainda correndo, cancelado, período anterior.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DashboardPeriodTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset From = Now.AddDays(-10);
    private static readonly DateTimeOffset Before = Now.AddDays(1);

    [Fact]
    public async Task Calcula_cumprimento_reabertura_e_o_periodo_anterior()
    {
        var world = await SetUpAsync();

        // Resposta e resolução dentro do prazo, e reaberto depois.
        var onTime = await TicketAsync(world, created: -5, dueHours: 8, respondedAfter: 1, resolvedAfter: 2, technician: world.TechnicianId);
        await ReopenedAsync(onTime, at: -1);

        // Resposta e resolução fora do prazo.
        await TicketAsync(world, created: -4, dueHours: 8, respondedAfter: 9, resolvedAfter: 30, technician: world.TechnicianId);

        // Sem resposta, prazo vencido: conta como não cumprido.
        await TicketAsync(world, created: -3, dueHours: 8);

        // Sem resposta, prazo ainda correndo: não conta nem a favor nem contra.
        await TicketAsync(world, created: 0, dueHours: 48);

        // Cancelado: fora de tudo.
        await TicketAsync(world, created: -2, dueHours: 8, cancelled: true);

        // Período anterior.
        await TicketAsync(world, created: -15, dueHours: 8, respondedAfter: 1, resolvedAfter: 2);

        var period = await PeriodAsync(world.Manager);
        var current = period.Current;

        Assert.Equal(4, current.Opened);
        Assert.Equal(1, current.ResponseMet);
        Assert.Equal(2, current.ResponseMissed);
        Assert.Equal(33.3, current.ResponseCompliance);

        Assert.Equal(2, current.Resolved);
        Assert.Equal(1, current.ResolutionMet);
        Assert.Equal(50.0, current.ResolutionCompliance);

        Assert.Equal(1, current.Reopened);
        Assert.Equal(50.0, current.ReopenRate);

        Assert.Equal(1, period.Previous.Opened);
        Assert.Equal(100.0, period.Previous.ResponseCompliance);

        var technician = Assert.Single(period.ByTechnician);
        Assert.Equal(world.TechnicianId, technician.Id);
        Assert.Equal(2, technician.Resolved);
        Assert.Equal(50.0, technician.ResolutionCompliance);
        Assert.Equal(1, technician.Reopened);
    }

    [Fact]
    public async Task Sem_prazo_decidido_o_percentual_e_vazio_e_nao_zero()
    {
        var world = await SetUpAsync();
        await TicketAsync(world, created: 0, dueHours: 48);

        var current = (await PeriodAsync(world.Manager)).Current;

        Assert.Null(current.ResponseCompliance);
        Assert.Null(current.ResolutionCompliance);
        Assert.Null(current.ReopenRate);
    }

    [Fact]
    public async Task Periodo_invertido_ou_longo_demais_e_recusado()
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await world.Manager.GetAsync(Url(Before, From))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await world.Manager.GetAsync(Url(Now.AddDays(-400), Now))).StatusCode);
    }

    [Fact]
    public async Task So_gestor_ve_os_indicadores_do_periodo()
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await world.Technician.GetAsync(Url(From, Before))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await world.Requester.GetAsync(Url(From, Before))).StatusCode);
    }

    [Fact]
    public async Task Aceita_o_periodo_com_deslocamento_de_sao_paulo()
    {
        var world = await SetUpAsync();

        var response = await world.Manager.GetAsync(
            $"/api/dashboard/period?from={Uri.EscapeDataString("2026-09-01T00:00:00-03:00")}&before={Uri.EscapeDataString("2026-10-01T00:00:00-03:00")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ----- Atalhos -----

    /// <summary>Um chamado com datas relativas a agora: dias para a abertura, horas para o resto.</summary>
    private async Task<Guid> TicketAsync(
        World world, int created, int dueHours, int? respondedAfter = null, int? resolvedAfter = null,
        Guid? technician = null, bool cancelled = false)
    {
        var id = await OpenTicketAsync(world);
        var createdAt = Now.AddDays(created);

        await using var db = Fixture.CreateContext();

        await db.Tickets.Where(t => t.Id == id).ExecuteUpdateAsync(set => set
            .SetProperty(t => t.CreatedAt, createdAt)
            .SetProperty(t => t.SlaResponseDueAt, createdAt.AddHours(dueHours))
            .SetProperty(t => t.SlaResolutionDueAt, createdAt.AddHours(dueHours * 2))
            .SetProperty(t => t.FirstRespondedAt, respondedAfter == null ? null : createdAt.AddHours(respondedAfter.Value))
            .SetProperty(t => t.ResolvedAt, resolvedAfter == null ? null : createdAt.AddHours(resolvedAfter.Value))
            .SetProperty(t => t.AssignedTechnicianId, technician)
            .SetProperty(t => t.Status, cancelled ? TicketStatus.Cancelled
                : resolvedAfter != null ? TicketStatus.Resolved : TicketStatus.Open));

        return id;
    }

    private async Task ReopenedAsync(Guid ticket, int at)
    {
        await using var db = Fixture.CreateContext();

        db.TicketHistory.Add(new TicketHistory
        {
            TicketId = ticket,
            Action = TicketHistoryAction.Reopened,
            PreviousValue = nameof(TicketStatus.Resolved),
            NewValue = nameof(TicketStatus.InProgress),
            CreatedAt = Now.AddDays(at)
        });

        await db.SaveChangesAsync();
    }

    private static string Url(DateTimeOffset from, DateTimeOffset before) =>
        $"/api/dashboard/period?from={Uri.EscapeDataString(from.ToString("O"))}&before={Uri.EscapeDataString(before.ToString("O"))}";

    private static async Task<DashboardPeriod> PeriodAsync(HttpClient manager)
    {
        var response = await manager.GetAsync(Url(From, Before));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<DashboardPeriod>())!;
    }
}
