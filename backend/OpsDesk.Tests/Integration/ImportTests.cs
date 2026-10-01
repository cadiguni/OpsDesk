using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Api.Endpoints;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Common;
using OpsDesk.Application.Dashboard;
using OpsDesk.Application.Import;
using OpsDesk.Application.Sla;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Importação de chamados de outra ferramenta (README, seção 13.14): conferência que não
/// grava, importação que não duplica, solicitante novo sem senha e chamado importado fora
/// dos indicadores de SLA.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ImportTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    private const string Header =
        "codigo_externo;titulo;descricao;solicitante_email;solicitante_nome;categoria;prioridade;status;responsavel_email;criado_em;resolvido_em";

    [Fact]
    public async Task Conferencia_nao_grava_nada_e_aponta_a_linha_com_problema()
    {
        var world = await SetUpAsync();
        var requester = await EmailOfAsync(world.RequesterId);

        var csv = $"""
            {Header}
            A-1;VPN cai;;nova.pessoa@empresa.com;Nova Pessoa;VPN;Alta;Aberto;;;
            A-2;Mouse;;{requester};;;;;;;
            A-3;Teclado;;{requester};;;Altíssima;;;;
            """;

        var report = await ImportAsync(world.Manager, csv, dryRun: true);

        Assert.True(report.DryRun);
        Assert.Equal(3, report.Rows);
        Assert.Equal(2, report.Valid);
        Assert.Equal(0, report.Created);
        Assert.Equal(1, report.NewRequesters);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(4, issue.Line);
        Assert.Contains("Altíssima", issue.Message);

        await using var db = Fixture.CreateContext();
        Assert.False(await db.Tickets.AnyAsync());
        Assert.False(await db.Users.AnyAsync(u => u.Email == "nova.pessoa@empresa.com"));
    }

    [Fact]
    public async Task Importa_com_solicitante_novo_sem_senha_categoria_por_nome_e_hora_de_sao_paulo()
    {
        var world = await SetUpAsync();
        var requester = await EmailOfAsync(world.RequesterId);
        var technician = await EmailOfAsync(world.TechnicianId);

        var csv = $"""
            {Header}
            A-1;VPN cai;Cai a cada hora;Nova.Pessoa@Empresa.com;Nova Pessoa;vpn;Alta;Resolvido;{technician};2025-03-10 09:00;2025-03-10 15:30
            A-2;Mouse;;{requester};;Categoria que não existe;;Aberto;;;
            """;

        var report = await ImportAsync(world.Manager, csv);

        Assert.Equal(2, report.Created);
        Assert.Equal(1, report.NewRequesters);
        Assert.Empty(report.Issues);

        await using var db = Fixture.CreateContext();

        var vpn = await db.Tickets.Include(t => t.Category).SingleAsync(t => t.ExternalId == "A-1");
        Assert.Equal(TicketSource.Csv, vpn.Source);
        Assert.Equal(TicketPriority.High, vpn.Priority);
        Assert.Equal(TicketStatus.Resolved, vpn.Status);
        Assert.Equal("VPN", vpn.Category.Name);
        Assert.Equal(world.TechnicianId, vpn.AssignedTechnicianId);
        Assert.StartsWith("OPS-", vpn.Code);
        Assert.Contains("Importado de arquivo (#A-1)", vpn.Description);

        // Data sem fuso é hora de São Paulo: 09:00 lá é 12:00 em UTC.
        Assert.Equal(new DateTimeOffset(2025, 3, 10, 12, 0, 0, TimeSpan.Zero), vpn.CreatedAt);
        Assert.Equal(new DateTimeOffset(2025, 3, 10, 18, 30, 0, TimeSpan.Zero), vpn.ResolvedAt);

        var newcomer = await db.Users.SingleAsync(u => u.Email == "nova.pessoa@empresa.com");
        Assert.Null(newcomer.PasswordHash);
        Assert.Equal(UserRole.Requester, newcomer.Role);
        Assert.Equal("Nova Pessoa", newcomer.Name);
        Assert.Equal(newcomer.Id, vpn.RequesterId);

        var mouse = await db.Tickets.Include(t => t.Category).SingleAsync(t => t.ExternalId == "A-2");
        Assert.Equal("Outros", mouse.Category.Name);
        Assert.Equal(TicketPriority.Medium, mouse.Priority);
        Assert.Equal(TicketStatus.Open, mouse.Status);
        Assert.Equal(world.RequesterId, mouse.RequesterId);

        // A abertura entra no histórico, como qualquer chamado.
        Assert.True(await db.TicketHistory.AnyAsync(h => h.TicketId == mouse.Id && h.Action == TicketHistoryAction.Created));

        // Importar não é evento: ninguém recebe aviso nem e-mail de chamado de anos atrás.
        Assert.False(await db.Notifications.AnyAsync());
        Assert.False(await db.OutboundEmails.AnyAsync());
    }

    [Fact]
    public async Task Chamado_importado_segue_a_visibilidade_de_sempre()
    {
        var world = await SetUpAsync();
        var requester = await EmailOfAsync(world.RequesterId);

        await ImportAsync(world.Manager, $"""
            {Header}
            A-1;Mouse;;{requester};;;;;;;
            """);

        Assert.Single((await ListAsync(world.Requester)).Items);
        Assert.Empty((await ListAsync(world.OtherRequester)).Items);
        Assert.Single((await ListAsync(world.Technician)).Items);
    }

    [Fact]
    public async Task Importar_o_mesmo_arquivo_de_novo_nao_duplica()
    {
        var world = await SetUpAsync();
        var requester = await EmailOfAsync(world.RequesterId);

        var csv = $"""
            {Header}
            A-1;VPN;;{requester};;;;;;;
            A-2;Mouse;;{requester};;;;;;;
            """;

        await ImportAsync(world.Manager, csv);
        var again = await ImportAsync(world.Manager, csv);

        Assert.Equal(0, again.Created);
        Assert.Equal(2, again.AlreadyImported);

        await using var db = Fixture.CreateContext();
        Assert.Equal(2, await db.Tickets.CountAsync());
    }

    [Fact]
    public async Task Exportacao_do_freshdesk_traduz_status_prioridade_e_agente_pelo_nome()
    {
        var world = await SetUpAsync();
        var requester = await EmailOfAsync(world.RequesterId);

        // Os dois técnicos do cenário têm o mesmo nome; o gestor tem nome único.
        var csv = $"""
            Ticket ID,Subject,Description,Status,Priority,Source,Type,Agent,Group,Created time,Resolved time,Closed time,Full name,Email
            101,"Erro no ERP, tela branca",Ao abrir o módulo fiscal,Pending,Urgent,Email,Sistemas,Usuário Manager,N1,2025-05-02 10:15:00,,,Fulano,{requester}
            102,Troca de toner,,Closed,Low,Portal,,Usuário Technician,N1,2025-05-03 08:00:00,2025-05-03 09:00:00,2025-05-05 09:00:00,Fulano,{requester}
            """;

        var report = await ImportAsync(world.Manager, csv, format: ImportFormat.Freshdesk);

        Assert.Equal(2, report.Created);
        Assert.Contains("status", report.RecognizedColumns);

        await using var db = Fixture.CreateContext();

        var erp = await db.Tickets.SingleAsync(t => t.ExternalId == "101");
        Assert.Equal(TicketSource.Freshdesk, erp.Source);
        Assert.Equal("Erro no ERP, tela branca", erp.Title);
        Assert.Equal(TicketStatus.WaitingOnRequester, erp.Status);
        Assert.Equal(TicketPriority.Critical, erp.Priority);
        Assert.Equal(world.ManagerId, erp.AssignedTechnicianId);

        var toner = await db.Tickets.SingleAsync(t => t.ExternalId == "102");
        Assert.Equal(TicketStatus.Closed, toner.Status);
        Assert.NotNull(toner.ResolvedAt);
        Assert.NotNull(toner.ClosedAt);

        // Nome ambíguo não é adivinhado.
        Assert.Null(toner.AssignedTechnicianId);
    }

    [Fact]
    public async Task Json_no_formato_do_opsdesk_tambem_entra()
    {
        var world = await SetUpAsync();
        var requester = await EmailOfAsync(world.RequesterId);

        var json = $$"""
            [
              { "codigo_externo": 7, "titulo": "Monitor piscando", "solicitante_email": "{{requester}}", "prioridade": "Crítica" }
            ]
            """;

        var report = await ImportAsync(world.Manager, json, fileName: "chamados.json");

        Assert.Equal(1, report.Created);

        await using var db = Fixture.CreateContext();
        var ticket = await db.Tickets.SingleAsync();
        Assert.Equal(TicketSource.ExternalApi, ticket.Source);
        Assert.Equal("7", ticket.ExternalId);
        Assert.Equal(TicketPriority.Critical, ticket.Priority);
    }

    [Fact]
    public async Task Sem_coluna_obrigatoria_a_previa_diz_qual_falta()
    {
        var world = await SetUpAsync();

        var report = await ImportAsync(world.Manager, "titulo;descricao\nVPN;cai\n", dryRun: true);

        Assert.Equal(["solicitante_email"], report.MissingColumns);
        Assert.Equal(0, report.Valid);
    }

    // ----- SLA -----

    [Fact]
    public async Task Chamado_importado_fica_fora_dos_indicadores_de_sla()
    {
        var world = await SetUpAsync();
        var requester = await EmailOfAsync(world.RequesterId);

        // Aberto há mais de um ano, sem resposta: vencidíssimo, se fosse medido.
        await ImportAsync(world.Manager, $"""
            {Header}
            A-1;VPN;;{requester};;;Crítica;Aberto;;2025-01-06 09:00;
            """);

        var dashboard = await world.Manager.GetFromJsonAsync<DashboardSummary>("/api/dashboard", TestJson.Options);
        Assert.Equal(0, dashboard!.OverdueResponse);
        Assert.Equal(0, dashboard.OverdueResolution);

        Assert.Empty((await ListAsync(world.Manager, "?overdue=true")).Items);
    }

    [Fact]
    public async Task Chamado_importado_nao_dispara_alerta_de_sla()
    {
        var world = await SetUpAsync();
        var portal = await OpenTicketAsync(world);
        var imported = await OpenTicketAsync(world, "Outro chamado");

        // Os dois com o prazo de resposta vencido há cinco minutos; só a origem muda.
        var now = DateTimeOffset.UtcNow;

        await using (var db = Fixture.CreateContext())
        {
            await db.Tickets.ExecuteUpdateAsync(set => set
                .SetProperty(t => t.SlaResponseDueAt, now.AddMinutes(-5))
                .SetProperty(t => t.SlaResolutionDueAt, now.AddDays(9)));

            await db.Tickets.Where(t => t.Id == imported).ExecuteUpdateAsync(set => set
                .SetProperty(t => t.Source, TicketSource.Freshservice)
                .SetProperty(t => t.ExternalId, "FS-1"));
        }

        using var scope = Fixture.Api.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<SlaAlertService>().RunAsync());

        await using var check = Fixture.CreateContext();
        Assert.Equal(portal, (await check.SlaAlerts.SingleAsync()).TicketId);
    }

    // ----- Quem pode -----

    [Fact]
    public async Task So_o_gestor_importa()
    {
        var world = await SetUpAsync();

        foreach (var client in new[] { world.Technician, world.Requester })
        {
            var response = await PostAsync(client, "titulo;solicitante_email\nVPN;a@empresa.com\n", dryRun: true);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Gestor_com_senha_provisoria_nao_importa()
    {
        await SetUpAsync();

        var email = $"gestor.{Guid.NewGuid():N}@empresa.com";

        await using (var db = Fixture.CreateContext())
        {
            var manager = new User { Name = "Gestor provisório", Email = email, Role = UserRole.Manager, MustChangePassword = true };
            manager.PasswordHash = new PasswordHasher<User>().HashPassword(manager, Password);
            db.Users.Add(manager);
            await db.SaveChangesAsync();
        }

        var client = Fixture.Api.CreateBrowserClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        var session = await login.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var response = await PostAsync(client, "titulo;solicitante_email\nVPN;a@empresa.com\n", dryRun: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ----- Atalhos -----

    private async Task<string> EmailOfAsync(Guid userId)
    {
        await using var db = Fixture.CreateContext();

        return await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync();
    }

    private static async Task<ImportReport> ImportAsync(
        HttpClient client,
        string content,
        string fileName = "chamados.csv",
        ImportFormat format = ImportFormat.OpsDesk,
        bool dryRun = false)
    {
        var response = await PostAsync(client, content, fileName, format, dryRun);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<ImportReport>())!;
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string content,
        string fileName = "chamados.csv",
        ImportFormat format = ImportFormat.OpsDesk,
        bool dryRun = false)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", fileName);

        return client.PostAsync($"/api/admin/import/tickets?format={format}&dryRun={dryRun.ToString().ToLowerInvariant()}", form);
    }
}
