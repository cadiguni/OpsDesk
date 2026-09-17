using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpsDesk.Api.Endpoints;
using OpsDesk.Application.Common;
using OpsDesk.Application.Tickets;
using OpsDesk.Application.Users;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Infrastructure.Persistence.Seed;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Abertura, listagem e detalhe do chamado por HTTP.
///
/// A ênfase está nos casos negativos de autorização: o que cada perfil <b>não</b> pode
/// ver. É o que o CLAUDE.md exige, e é onde um erro deixa de ser bug e passa a ser
/// vazamento de dado.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TicketEndpointsTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    // ----- Abertura -----

    [Fact]
    public async Task Abertura_preenche_os_campos_automaticos()
    {
        var world = await SetUpAsync();

        var response = await world.Requester.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Não consigo acessar a VPN",
            "Desde hoje o cliente de VPN recusa minha credencial.",
            world.CategoryId,
            TicketPriority.High));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var ticket = await response.Content.ReadJsonAsync<TicketDetail>();

        Assert.NotNull(ticket);
        Assert.Matches(@"^OPS-\d{6}$", ticket.Code);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(TicketSource.Portal, ticket.Source);
        Assert.Equal(world.RequesterId, ticket.RequesterId);
        Assert.Null(ticket.AssignedTechnicianId);
        Assert.Null(ticket.FirstRespondedAt);
        Assert.False(ticket.SlaPaused);

        // Prazos calculados na criação, resposta antes da resolução.
        Assert.True(ticket.SlaResponseDueAt > ticket.CreatedAt);
        Assert.True(ticket.SlaResolutionDueAt > ticket.SlaResponseDueAt);
    }

    [Fact]
    public async Task Abertura_usa_o_prazo_da_politica_da_prioridade()
    {
        // Crítica: uma hora útil para resposta, quatro para resolução. Se o serviço pegar
        // a política errada, o chamado nasce com o prazo de outra prioridade e o indicador
        // de vencidos mente desde o primeiro dia.
        var world = await SetUpAsync();

        var response = await world.Requester.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Sistema de faturamento fora do ar",
            "Nenhum usuário consegue emitir nota.",
            world.CategoryId,
            TicketPriority.Critical));

        var ticket = await response.Content.ReadJsonAsync<TicketDetail>();

        // Fora do expediente a contagem começa na próxima abertura, então o prazo em horas
        // de parede pode ser bem maior que o prazo em horas úteis. A comparação é de piso e
        // de ordem, e não de igualdade.
        Assert.True(ticket!.SlaResponseDueAt >= ticket.CreatedAt.AddHours(1));
        Assert.True(ticket.SlaResolutionDueAt >= ticket.CreatedAt.AddHours(4));
        Assert.True(ticket.SlaResolutionDueAt > ticket.SlaResponseDueAt);

        // E o prazo vem do banco em UTC, como toda data do sistema.
        Assert.Equal(TimeSpan.Zero, ticket.SlaResponseDueAt.Offset);
    }

    [Fact]
    public async Task Abertura_grava_o_historico_de_criacao()
    {
        var world = await SetUpAsync();

        var response = await world.Requester.PostAsJsonAsync("/api/tickets", NewTicket(world.CategoryId));
        var ticket = await response.Content.ReadJsonAsync<TicketDetail>();

        await using var db = Fixture.CreateContext();
        var history = await db.TicketHistory.Where(h => h.TicketId == ticket!.Id).ToListAsync();

        var entry = Assert.Single(history);
        Assert.Equal(TicketHistoryAction.Created, entry.Action);
        Assert.Equal(world.RequesterId, entry.ChangedById);
    }

    [Fact]
    public async Task Abertura_em_categoria_inexistente_e_recusada()
    {
        var world = await SetUpAsync();

        var response = await world.Requester.PostAsJsonAsync(
            "/api/tickets", NewTicket(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Abertura_em_categoria_inativa_e_recusada()
    {
        var world = await SetUpAsync();

        await using var db = Fixture.CreateContext();
        await db.Categories
            .Where(c => c.Id == world.CategoryId)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.IsActive, false));

        var response = await world.Requester.PostAsJsonAsync(
            "/api/tickets", NewTicket(world.CategoryId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Descrição suficientemente longa.")]
    [InlineData("Título válido", "curta")]
    public async Task Abertura_invalida_devolve_erro_de_validacao(string title, string description)
    {
        var world = await SetUpAsync();

        var response = await world.Requester.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            title, description, world.CategoryId, TicketPriority.Medium));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Corpo_malformado_devolve_400_e_nao_500()
    {
        // 500 significa "defeito nosso" e alimenta alerta de produção. Corpo inválido é
        // erro do cliente, e a diferença tem que aparecer no monitoramento.
        var world = await SetUpAsync();

        using var content = new StringContent(
            "{ \"title\": ", System.Text.Encoding.UTF8, "application/json");

        var response = await world.Requester.PostAsync("/api/tickets", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Abertura_sem_autenticacao_e_recusada()
    {
        var world = await SetUpAsync();

        using var anonymous = Fixture.Api.CreateBrowserClient();
        var response = await anonymous.PostAsJsonAsync("/api/tickets", NewTicket(world.CategoryId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sem_solicitante_no_corpo_o_solicitante_e_quem_esta_autenticado()
    {
        var world = await SetUpAsync();

        var response = await world.OtherRequester.PostAsJsonAsync(
            "/api/tickets", NewTicket(world.CategoryId));
        var ticket = await response.Content.ReadJsonAsync<TicketDetail>();

        Assert.Equal(world.OtherRequesterId, ticket!.RequesterId);
    }

    // ----- Abertura em nome de outra pessoa -----

    [Fact]
    public async Task Tecnico_abre_chamado_em_nome_do_solicitante()
    {
        // O caso real é o atendimento por telefone ou presencial: quem registra é o
        // técnico, mas o chamado é do usuário — e precisa aparecer na lista dele.
        var world = await SetUpAsync();

        var response = await world.Technician.PostAsJsonAsync(
            "/api/tickets",
            NewTicket(world.CategoryId) with { RequesterId = world.RequesterId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var ticket = await response.Content.ReadJsonAsync<TicketDetail>();

        Assert.Equal(world.RequesterId, ticket!.RequesterId);
        Assert.Equal(TicketSource.Portal, ticket.Source);

        // Sem responsável: registrar em nome de alguém não é assumir o atendimento.
        Assert.Null(ticket.AssignedTechnicianId);

        var page = await ListAsync(world.Requester);
        Assert.Contains(page.Items, item => item.Id == ticket.Id);
    }

    [Fact]
    public async Task Quem_abriu_em_nome_de_outro_fica_no_historico()
    {
        // O campo Requester diz de quem é o problema; quem digitou está na auditoria.
        var world = await SetUpAsync();

        var response = await world.Technician.PostAsJsonAsync(
            "/api/tickets",
            NewTicket(world.CategoryId) with { RequesterId = world.RequesterId });

        var ticket = await response.Content.ReadJsonAsync<TicketDetail>();
        var history = await HistoryAsync(world.Technician, ticket!.Id);

        var created = Assert.Single(
            history, h => h.Action == TicketHistoryAction.Created);

        Assert.Equal(world.TechnicianId, created.ChangedById);
    }

    [Fact]
    public async Task Solicitante_nao_abre_chamado_em_nome_de_outra_pessoa()
    {
        var world = await SetUpAsync();

        var response = await world.Requester.PostAsJsonAsync(
            "/api/tickets",
            NewTicket(world.CategoryId) with { RequesterId = world.OtherRequesterId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // E nada foi gravado: o chamado não existe nem para o suposto solicitante.
        var page = await ListAsync(world.OtherRequester);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Abertura_em_nome_de_usuario_inexistente_e_recusada()
    {
        var world = await SetUpAsync();

        var response = await world.Technician.PostAsJsonAsync(
            "/api/tickets",
            NewTicket(world.CategoryId) with { RequesterId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Abertura_em_nome_de_usuario_inativo_e_recusada()
    {
        var world = await SetUpAsync();

        await using (var db = Fixture.CreateContext())
        {
            await db.Users
                .Where(u => u.Id == world.RequesterId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
        }

        var response = await world.Technician.PostAsJsonAsync(
            "/api/tickets",
            NewTicket(world.CategoryId) with { RequesterId = world.RequesterId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Informar_o_proprio_id_como_solicitante_e_aceito()
    {
        // Não é abertura em nome de terceiro: a interface pode mandar o campo preenchido
        // com o próprio usuário sem que isso vire privilégio de equipe.
        var world = await SetUpAsync();

        var response = await world.Requester.PostAsJsonAsync(
            "/api/tickets",
            NewTicket(world.CategoryId) with { RequesterId = world.RequesterId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ----- Diretório de usuários -----

    [Fact]
    public async Task Solicitante_nao_enumera_usuarios()
    {
        var world = await SetUpAsync();

        var response = await world.Requester.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Equipe_busca_usuario_por_nome_ou_email()
    {
        var world = await SetUpAsync();

        string email;
        await using (var db = Fixture.CreateContext())
        {
            email = await db.Users
                .Where(u => u.Id == world.RequesterId)
                .Select(u => u.Email)
                .SingleAsync();
        }

        var response = await world.Technician.GetAsync($"/api/users?search={email}");

        response.EnsureSuccessStatusCode();

        var users = await response.Content.ReadJsonAsync<List<UserOption>>();

        var found = Assert.Single(users!);
        Assert.Equal(world.RequesterId, found.Id);
    }

    [Fact]
    public async Task Usuario_inativo_nao_aparece_no_diretorio()
    {
        var world = await SetUpAsync();

        await using (var db = Fixture.CreateContext())
        {
            await db.Users
                .Where(u => u.Id == world.RequesterId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
        }

        var response = await world.Manager.GetAsync("/api/users");
        var users = await response.Content.ReadJsonAsync<List<UserOption>>();

        Assert.DoesNotContain(users!, u => u.Id == world.RequesterId);
    }

    // ----- Listagem e visibilidade -----

    [Fact]
    public async Task Solicitante_lista_apenas_os_proprios_chamados()
    {
        var world = await SetUpWithTicketsAsync();

        var page = await ListAsync(world.Requester);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Chamado do solicitante", page.Items[0].Title);
    }

    [Fact]
    public async Task Tecnico_lista_a_fila_inteira()
    {
        var world = await SetUpWithTicketsAsync();

        var page = await ListAsync(world.Technician);
        var titles = page.Items.Select(t => t.Title).ToList();

        Assert.Contains("Chamado do solicitante", titles);
        Assert.Contains("Sem responsável", titles);
        Assert.Contains("Atribuído ao técnico", titles);
        Assert.Contains("De outro técnico", titles);
        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public async Task Tecnico_filtra_o_que_e_dele_quando_quiser()
    {
        // Ver tudo não é o mesmo que trabalhar sobre tudo: a fila pessoal continua
        // disponível, agora como filtro em vez de imposição do backend.
        var world = await SetUpWithTicketsAsync();

        var page = await ListAsync(
            world.Technician, $"?assignedTechnicianId={world.TechnicianId}");

        var titles = page.Items.Select(t => t.Title).ToList();

        Assert.Equal(["Atribuído ao técnico"], titles);
    }

    [Fact]
    public async Task Gestor_lista_todos_os_chamados()
    {
        var world = await SetUpWithTicketsAsync();

        var page = await ListAsync(world.Manager);

        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public async Task Solicitante_nao_alcanca_chamado_de_terceiro_pelo_id()
    {
        // O caso que mais importa: adivinhar o identificador não dá acesso, e a resposta é
        // 404 — um 403 confirmaria que aquele chamado existe.
        var world = await SetUpWithTicketsAsync();

        var response = await world.Requester.GetAsync($"/api/tickets/{world.OfOtherTechnicianId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tecnico_alcanca_pelo_id_o_chamado_que_outro_tecnico_assumiu()
    {
        var world = await SetUpWithTicketsAsync();

        var response = await world.Technician.GetAsync($"/api/tickets/{world.OfOtherTechnicianId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Chamado_inexistente_e_chamado_de_terceiro_respondem_igual()
    {
        var world = await SetUpWithTicketsAsync();

        // Pela ótica do solicitante: um chamado que não é dele e um que não existe.
        var missing = await world.Requester.GetAsync($"/api/tickets/{Guid.CreateVersion7()}");
        var forbidden = await world.Requester.GetAsync($"/api/tickets/{world.OfOtherTechnicianId}");

        Assert.Equal(missing.StatusCode, forbidden.StatusCode);
    }

    [Fact]
    public async Task Solicitante_ve_o_proprio_chamado_pelo_id()
    {
        var world = await SetUpWithTicketsAsync();

        var response = await world.Requester.GetAsync($"/api/tickets/{world.OwnTicketId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ticket = await response.Content.ReadJsonAsync<TicketDetail>();
        Assert.Equal("Chamado do solicitante", ticket!.Title);
    }

    // ----- Filtros, ordenação e paginação -----

    [Fact]
    public async Task Filtro_de_status_e_aplicado()
    {
        var world = await SetUpWithTicketsAsync();

        var page = await ListAsync(world.Manager, "?status=InProgress");

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(TicketStatus.InProgress, page.Items[0].Status);
    }

    [Fact]
    public async Task Filtro_de_status_aceita_varios_valores()
    {
        var world = await SetUpWithTicketsAsync();

        var page = await ListAsync(world.Manager, "?status=Open&status=InProgress");

        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public async Task Filtro_de_sem_responsavel_e_aplicado()
    {
        var world = await SetUpWithTicketsAsync();

        var page = await ListAsync(world.Manager, "?unassigned=true");

        Assert.All(page.Items, item => Assert.Null(item.AssignedTechnicianName));
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task Busca_encontra_por_codigo_e_por_titulo_sem_distinguir_caixa()
    {
        var world = await SetUpWithTicketsAsync();

        var byTitle = await ListAsync(world.Manager, "?search=SOLICITANTE");
        Assert.Equal(1, byTitle.TotalCount);

        var code = byTitle.Items[0].Code;
        var byCode = await ListAsync(world.Manager, $"?search={code.ToLowerInvariant()}");

        Assert.Equal(1, byCode.TotalCount);
        Assert.Equal(code, byCode.Items[0].Code);
    }

    [Fact]
    public async Task Filtro_de_vencidos_ignora_chamado_cancelado()
    {
        // README, seção 8.3: chamado cancelado fica fora dos indicadores de SLA. Sem esta
        // exclusão, o painel de vencidos passaria a contar chamado que ninguém vai atender.
        var world = await SetUpAsync();

        var overdue = await OpenTicketAsync(world, "Vencido de verdade");
        var cancelled = await OpenTicketAsync(world, "Vencido mas cancelado");

        await using var db = Fixture.CreateContext();
        var past = DateTimeOffset.UtcNow.AddDays(-5);

        await db.Tickets
            .Where(t => t.Id == overdue || t.Id == cancelled)
            .ExecuteUpdateAsync(t => t
                .SetProperty(x => x.SlaResponseDueAt, past)
                .SetProperty(x => x.SlaResolutionDueAt, past));

        await db.Tickets
            .Where(t => t.Id == cancelled)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.Status, TicketStatus.Cancelled));

        var page = await ListAsync(world.Manager, "?overdue=true");

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Vencido de verdade", page.Items[0].Title);
    }

    [Fact]
    public async Task Ordenacao_por_prioridade_usa_urgencia_e_nao_ordem_alfabetica()
    {
        // A prioridade é persistida como texto: ordenar pela coluna daria Critical, High,
        // Low, Medium. A ordem esperada é a de urgência.
        var world = await SetUpAsync();

        await OpenTicketAsync(world, "Baixa", TicketPriority.Low);
        await OpenTicketAsync(world, "Crítica", TicketPriority.Critical);
        await OpenTicketAsync(world, "Média", TicketPriority.Medium);
        await OpenTicketAsync(world, "Alta", TicketPriority.High);

        var page = await ListAsync(world.Manager, "?sort=PriorityDescending");

        Assert.Equal(
            [TicketPriority.Critical, TicketPriority.High, TicketPriority.Medium, TicketPriority.Low],
            page.Items.Select(i => i.Priority));
    }

    [Fact]
    public async Task Paginacao_respeita_o_limite_maximo_do_servidor()
    {
        var world = await SetUpAsync();

        for (var i = 0; i < 3; i++)
        {
            await OpenTicketAsync(world, $"Chamado {i}");
        }

        // Pedido absurdo é reduzido em silêncio, nunca honrado.
        var page = await ListAsync(world.Manager, "?pageSize=10000");

        Assert.Equal(PageRequest.MaxPageSize, page.PageSize);
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task Paginacao_nao_repete_nem_perde_chamado_entre_paginas()
    {
        // Sem ordenação total, duas linhas com o mesmo CreatedAt podem trocar de lugar e um
        // chamado aparecer duas vezes ou nenhuma. Os chamados abaixo nascem no mesmo lote.
        var world = await SetUpAsync();

        for (var i = 0; i < 5; i++)
        {
            await OpenTicketAsync(world, $"Chamado {i}");
        }

        var first = await ListAsync(world.Manager, "?pageSize=2&page=1");
        var second = await ListAsync(world.Manager, "?pageSize=2&page=2");
        var third = await ListAsync(world.Manager, "?pageSize=2&page=3");

        var seen = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id).ToList();

        Assert.Equal(5, seen.Count);
        Assert.Equal(5, seen.Distinct().Count());
        Assert.Equal(5, first.TotalCount);
        Assert.True(first.HasNextPage);
        Assert.False(third.HasNextPage);
    }

    // ----- Transições oferecidas -----

    [Fact]
    public async Task O_detalhe_oferece_apenas_as_transicoes_do_perfil()
    {
        var world = await SetUpWithTicketsAsync();

        var asRequester = await GetAsync(world.Requester, world.OwnTicketId);
        var asManager = await GetAsync(world.Manager, world.OwnTicketId);

        // O solicitante só cancela o próprio chamado aberto.
        Assert.Equal([TicketStatus.Cancelled], asRequester.AllowedNextStatuses);

        // A equipe recebe o grafo inteiro a partir de Aberto.
        Assert.Contains(TicketStatus.Triage, asManager.AllowedNextStatuses);
        Assert.Contains(TicketStatus.InProgress, asManager.AllowedNextStatuses);
        Assert.Contains(TicketStatus.Cancelled, asManager.AllowedNextStatuses);
    }

    // ----- Categorias -----

    [Fact]
    public async Task Categorias_lista_apenas_as_ativas_em_ordem_alfabetica()
    {
        var world = await SetUpAsync();

        var response = await world.Requester.GetAsync("/api/categories");
        var categories = await response.Content.ReadJsonAsync<List<CategoryOption>>();

        Assert.NotNull(categories);
        Assert.All(categories, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
        Assert.Equal(categories.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal), categories.Select(c => c.Name));
    }

    [Fact]
    public async Task Categorias_exige_autenticacao()
    {
        await SetUpAsync();

        using var anonymous = Fixture.Api.CreateBrowserClient();
        var response = await anonymous.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Cenário de visibilidade: um chamado de cada combinação de solicitante e responsável.
    /// </summary>
    private async Task<World> SetUpWithTicketsAsync()
    {
        var world = await SetUpAsync();

        var own = await OpenTicketAsync(world, "Chamado do solicitante");
        var unassigned = await OpenTicketAsync(world, "Sem responsável", client: world.OtherRequester);
        var assigned = await OpenTicketAsync(world, "Atribuído ao técnico", client: world.OtherRequester);
        var other = await OpenTicketAsync(world, "De outro técnico", client: world.OtherRequester);

        await using var db = Fixture.CreateContext();

        await db.Tickets
            .Where(t => t.Id == assigned)
            .ExecuteUpdateAsync(t => t
                .SetProperty(x => x.AssignedTechnicianId, world.TechnicianId)
                .SetProperty(x => x.Status, TicketStatus.InProgress));

        await db.Tickets
            .Where(t => t.Id == other)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.AssignedTechnicianId, world.OtherTechnicianId));

        return world with
        {
            OwnTicketId = own,
            UnassignedTicketId = unassigned,
            OfOtherTechnicianId = other
        };
    }
}
