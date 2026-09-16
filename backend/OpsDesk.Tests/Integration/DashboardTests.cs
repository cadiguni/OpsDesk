using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Dashboard;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Indicadores do dashboard (README, seção 13.7).
/// </summary>
[Collection(PostgresCollection.Name)]
public class DashboardTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    [Fact]
    public async Task Somente_gestor_acessa_o_dashboard()
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.OK, (await world.Manager.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await world.Technician.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await world.Requester.GetAsync("/api/dashboard")).StatusCode);

        using var anonymous = Fixture.Api.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Conta_os_chamados_por_status()
    {
        var world = await SetUpAsync();

        var inProgress = await OpenTicketAsync(world, "Em atendimento");
        await ChangeStatusAsync(world.Technician, inProgress, TicketStatus.InProgress);

        var waiting = await OpenTicketAsync(world, "Aguardando");
        await ChangeStatusAsync(world.Technician, waiting, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, waiting, TicketStatus.WaitingOnRequester);

        await OpenTicketAsync(world, "Aberto 1");
        await OpenTicketAsync(world, "Aberto 2");

        var summary = await SummaryAsync(world);

        Assert.Equal(4, summary.Total);
        Assert.Equal(2, summary.Open);
        Assert.Equal(1, summary.InProgress);
        Assert.Equal(1, summary.WaitingOnRequester);
        Assert.Equal(0, summary.Resolved);
    }

    [Fact]
    public async Task Os_rotulos_de_status_saem_sempre_na_mesma_ordem()
    {
        // O gráfico não pode trocar as colunas de lugar entre duas cargas, e a ordem que o
        // banco devolve em um GROUP BY não é garantida.
        var world = await SetUpAsync();
        await OpenTicketAsync(world);

        var first = await SummaryAsync(world);
        var second = await SummaryAsync(world);

        Assert.Equal(
            Enum.GetValues<TicketStatus>().Select(s => s.ToString()),
            first.ByStatus.Select(x => x.Label));

        Assert.Equal(first.ByStatus.Select(x => x.Label), second.ByStatus.Select(x => x.Label));
    }

    [Fact]
    public async Task Status_sem_nenhum_chamado_aparece_com_zero()
    {
        // Omitir a coluna faria o gráfico mudar de forma conforme a operação — e esconderia
        // a informação de que aquele status está vazio, que é ela mesma um indicador.
        var world = await SetUpAsync();
        await OpenTicketAsync(world);

        var summary = await SummaryAsync(world);

        Assert.Equal(Enum.GetValues<TicketStatus>().Length, summary.ByStatus.Count);
        Assert.Contains(summary.ByStatus, x => x.Label == nameof(TicketStatus.Closed) && x.Count == 0);
    }

    [Fact]
    public async Task Conta_por_prioridade_com_as_quatro_sempre_presentes()
    {
        var world = await SetUpAsync();

        await OpenTicketAsync(world, "Crítico", TicketPriority.Critical);
        await OpenTicketAsync(world, "Alto 1", TicketPriority.High);
        await OpenTicketAsync(world, "Alto 2", TicketPriority.High);

        var summary = await SummaryAsync(world);

        Assert.Equal(4, summary.ByPriority.Count);
        Assert.Equal(1, Count(summary.ByPriority, nameof(TicketPriority.Critical)));
        Assert.Equal(2, Count(summary.ByPriority, nameof(TicketPriority.High)));
        Assert.Equal(0, Count(summary.ByPriority, nameof(TicketPriority.Low)));
    }

    [Fact]
    public async Task Conta_por_categoria()
    {
        var world = await SetUpAsync();

        await OpenTicketAsync(world, "VPN 1");
        await OpenTicketAsync(world, "VPN 2");

        var summary = await SummaryAsync(world);

        Assert.Equal(2, Count(summary.ByCategory, "VPN"));

        // Só categorias com chamado aparecem: doze linhas com zero não ajudariam ninguém.
        Assert.Single(summary.ByCategory);
    }

    [Fact]
    public async Task Conta_por_tecnico_e_inclui_a_fila_sem_responsavel()
    {
        var world = await SetUpAsync();

        var assigned = await OpenTicketAsync(world, "Atribuído");
        await AssignAsync(world.Manager, assigned, world.TechnicianId);

        await OpenTicketAsync(world, "Sem dono 1");
        await OpenTicketAsync(world, "Sem dono 2");

        var summary = await SummaryAsync(world);

        Assert.Equal(1, Count(summary.ByTechnician, "Usuário Technician"));

        // A fila sem responsável é o que o painel do técnico consome; omiti-la faria o
        // gráfico parecer cobrir a operação inteira quando não cobre.
        Assert.Equal(2, Count(summary.ByTechnician, "Sem responsável"));
    }

    [Fact]
    public async Task A_fila_sem_responsavel_nao_conta_chamado_encerrado()
    {
        var world = await SetUpAsync();

        var cancelled = await OpenTicketAsync(world, "Cancelado sem dono");
        await ChangeStatusAsync(world.Requester, cancelled, TicketStatus.Cancelled);

        await OpenTicketAsync(world, "Aberto sem dono");

        var summary = await SummaryAsync(world);

        Assert.Equal(1, Count(summary.ByTechnician, "Sem responsável"));
    }

    [Fact]
    public async Task Conta_vencidos_de_resposta_e_de_resolucao()
    {
        var world = await SetUpAsync();

        var overdue = await OpenTicketAsync(world, "Vencido");
        var onTime = await OpenTicketAsync(world, "No prazo");

        await using var db = Fixture.CreateContext();
        var past = DateTimeOffset.UtcNow.AddDays(-3);

        await db.Tickets
            .Where(t => t.Id == overdue)
            .ExecuteUpdateAsync(t => t
                .SetProperty(x => x.SlaResponseDueAt, past)
                .SetProperty(x => x.SlaResolutionDueAt, past));

        var summary = await SummaryAsync(world);

        Assert.Equal(1, summary.OverdueResponse);
        Assert.Equal(1, summary.OverdueResolution);
        Assert.NotEqual(Guid.Empty, onTime);
    }

    [Fact]
    public async Task Chamado_respondido_sai_dos_vencidos_de_resposta()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await using var db = Fixture.CreateContext();
        var past = DateTimeOffset.UtcNow.AddDays(-3);

        await db.Tickets
            .Where(t => t.Id == ticket)
            .ExecuteUpdateAsync(t => t
                .SetProperty(x => x.SlaResponseDueAt, past)
                .SetProperty(x => x.SlaResolutionDueAt, past));

        Assert.Equal(1, (await SummaryAsync(world)).OverdueResponse);

        // O primeiro comentário público da equipe encerra o SLA de resposta.
        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new Application.Tickets.AddCommentRequest("Estamos verificando."));

        var after = await SummaryAsync(world);

        Assert.Equal(0, after.OverdueResponse);
        Assert.Equal(1, after.OverdueResolution);
    }

    [Fact]
    public async Task Chamado_cancelado_fica_fora_dos_vencidos()
    {
        // README, seção 8.3. Sem esta exclusão, o indicador passaria a contar chamado que
        // ninguém vai atender, e a operação apareceria pior do que é.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await using var db = Fixture.CreateContext();
        var past = DateTimeOffset.UtcNow.AddDays(-3);

        await db.Tickets
            .Where(t => t.Id == ticket)
            .ExecuteUpdateAsync(t => t
                .SetProperty(x => x.SlaResponseDueAt, past)
                .SetProperty(x => x.SlaResolutionDueAt, past));

        Assert.Equal(1, (await SummaryAsync(world)).OverdueResponse);

        await ChangeStatusAsync(world.Requester, ticket, TicketStatus.Cancelled);

        var after = await SummaryAsync(world);

        Assert.Equal(0, after.OverdueResponse);
        Assert.Equal(0, after.OverdueResolution);
    }

    [Fact]
    public async Task Dashboard_vazio_nao_quebra()
    {
        var world = await SetUpAsync();

        var summary = await SummaryAsync(world);

        Assert.Equal(0, summary.Total);
        Assert.Empty(summary.ByCategory);
        Assert.Equal(4, summary.ByPriority.Count);

        // Só a linha "Sem responsável", com zero.
        Assert.Single(summary.ByTechnician);
    }

    private static async Task<DashboardSummary> SummaryAsync(World world)
    {
        var response = await world.Manager.GetAsync("/api/dashboard");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<DashboardSummary>())!;
    }

    private static int Count(IReadOnlyList<CountByLabel> counts, string label) =>
        counts.FirstOrDefault(x => x.Label == label)?.Count ?? 0;
}
