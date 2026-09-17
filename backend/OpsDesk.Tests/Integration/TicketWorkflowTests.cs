using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Atendimento do chamado: comentários, mudança de status e atribuição.
///
/// Concentra o que o CLAUDE.md marca como obrigatório — transições da máquina de estados,
/// casos negativos de autorização, SLA com pausa e retomada, e a ausência de comentário
/// interno nas respostas destinadas ao solicitante.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TicketWorkflowTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    // ----- Comentários -----

    [Theory]
    [InlineData(TicketStatus.Open, false)]
    [InlineData(TicketStatus.Triage, false)]
    [InlineData(TicketStatus.InProgress, false)]
    [InlineData(TicketStatus.WaitingOnRequester, false)]
    [InlineData(TicketStatus.Resolved, false)]
    [InlineData(TicketStatus.Open, true)]
    [InlineData(TicketStatus.WaitingOnRequester, true)]
    public async Task Enviar_e_fechar_grava_comentario_status_sla_e_historico(TicketStatus from, bool isInternal)
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        if (from == TicketStatus.Triage)
            await ChangeStatusAsync(world.Technician, ticket, from);
        else if (from != TicketStatus.Open)
        {
            await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
            if (from != TicketStatus.InProgress)
                await ChangeStatusAsync(world.Technician, ticket, from);
        }

        var before = await GetAsync(world.Manager, ticket);
        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Atendimento concluído.", isInternal, CloseTicket: true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = await GetAsync(world.Manager, ticket);
        Assert.Equal(TicketStatus.Closed, after.Status);
        Assert.NotNull(after.ClosedAt);
        Assert.NotNull(after.ResolvedAt);
        Assert.False(after.SlaPaused);
        Assert.Equal(before.ResolvedAt ?? after.ClosedAt, after.ResolvedAt);
        Assert.Equal(isInternal, after.FirstRespondedAt is null);
        Assert.Single(await CommentsAsync(world.Manager, ticket));
        var visible = await CommentsAsync(world.Requester, ticket);
        Assert.Equal(isInternal ? 0 : 1, visible.Count);
        var history = await HistoryAsync(world.Manager, ticket);

        // Uma entrada só para o fechamento, e ela carrega a transição real. O interceptor
        // dá nome próprio às transições terminais — fechar é `Closed`, não `StatusChanged`
        // com destino "Closed" —, e não simula as etapas intermediárias que o "enviar e
        // fechar" pulou.
        var closed = Assert.Single(history, h => h.Action == TicketHistoryAction.Closed);
        Assert.Equal(from.ToString(), closed.PreviousValue);
        Assert.Equal(nameof(TicketStatus.Closed), closed.NewValue);
        Assert.DoesNotContain(history, h => h.Action == TicketHistoryAction.StatusChanged && h.NewValue == "Closed");

        Assert.Contains(history, h => h.Action == (isInternal ? TicketHistoryAction.InternalCommentAdded : TicketHistoryAction.CommentAdded));
    }

    [Fact]
    public async Task Enviar_e_fechar_recusado_nao_grava_comentario_nem_altera_sla()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        var response = await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Fechar indevidamente.", CloseTicket: true));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await CommentsAsync(world.Manager, ticket));
        var after = await GetAsync(world.Manager, ticket);
        Assert.Equal(TicketStatus.Open, after.Status);
        Assert.Null(after.ResolvedAt);
        Assert.Null(after.ClosedAt);
        Assert.Null(after.FirstRespondedAt);
    }

    [Fact]
    public async Task Solicitante_envia_confirmacao_e_fecha_chamado_resolvido()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);
        var response = await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Funcionou, obrigado.", CloseTicket: true));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(TicketStatus.Closed, (await GetAsync(world.Manager, ticket)).Status);
    }

    [Theory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public async Task Enviar_e_fechar_recusa_terminais_sem_gravar(TicketStatus status)
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await ChangeStatusAsync(world.Manager, ticket, status);
        var response = await world.Manager.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Tarde demais.", CloseTicket: true));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await CommentsAsync(world.Manager, ticket));
    }

    [Fact]
    public async Task Enviar_e_fechar_valida_texto_e_visibilidade_antes_de_gravar()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        var empty = await world.Manager.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest(" ", CloseTicket: true));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var hidden = await world.OtherRequester.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Indevido.", CloseTicket: true));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Empty(await CommentsAsync(world.Manager, ticket));
        Assert.Equal(TicketStatus.Open, (await GetAsync(world.Manager, ticket)).Status);
    }

    [Fact]
    public async Task Tecnico_comenta_e_o_solicitante_ve()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var posted = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest("Poderia enviar um print do erro?"));

        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);

        var visible = await CommentsAsync(world.Requester, ticket);

        var comment = Assert.Single(visible);
        Assert.Equal("Poderia enviar um print do erro?", comment.Content);
        Assert.False(comment.IsInternal);
        Assert.Equal(UserRole.Technician, comment.AuthorRole);
    }

    [Fact]
    public async Task Comentario_interno_nunca_chega_ao_solicitante()
    {
        // Invariante 2 do CLAUDE.md.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest("Resposta ao usuário.", IsInternal: false));

        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest("Checar grupo do AD antes de responder.", IsInternal: true));

        var forRequester = await CommentsAsync(world.Requester, ticket);
        var forTechnician = await CommentsAsync(world.Technician, ticket);
        var forManager = await CommentsAsync(world.Manager, ticket);

        Assert.Single(forRequester);
        Assert.All(forRequester, c => Assert.False(c.IsInternal));
        Assert.DoesNotContain(forRequester, c => c.Content.Contains("AD"));

        Assert.Equal(2, forTechnician.Count);
        Assert.Equal(2, forManager.Count);
    }

    [Fact]
    public async Task O_texto_do_comentario_interno_nao_aparece_na_resposta_do_solicitante()
    {
        // Verificação sobre o JSON cru: um campo novo que vazasse o conteúdo passaria pelo
        // teste acima, que olha só a lista tipada.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest("segredo-da-equipe", IsInternal: true));

        var response = await world.Requester.GetAsync($"/api/tickets/{ticket}/comments");
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("segredo", json);
    }

    [Fact]
    public async Task Solicitante_nao_pode_criar_comentario_interno()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest("Tentando esconder isto.", IsInternal: true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // E nada foi gravado: recusar é diferente de converter em público em silêncio.
        await using var db = Fixture.CreateContext();
        Assert.Equal(0, await db.TicketComments.CountAsync());
    }

    [Fact]
    public async Task Solicitante_nao_comenta_em_chamado_de_terceiro()
    {
        var world = await SetUpAsync();
        var ofSomeoneElse = await OpenTicketAsync(world, client: world.OtherRequester);

        var response = await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{ofSomeoneElse}/comments",
            new AddCommentRequest("Comentário indevido."));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tecnico_comenta_em_chamado_de_outro_tecnico()
    {
        // Cobrir ausência de colega é atendimento normal, e antes exigia passar pelo
        // gestor: o chamado do outro técnico simplesmente não existia para este.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await AssignAsync(world.Manager, ticket, world.OtherTechnicianId);

        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest("Assumindo enquanto o colega está fora.", IsInternal: true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Comentario_gera_historico()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Resposta pública."));

        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest("Nota interna.", IsInternal: true));

        var history = await HistoryAsync(world.Manager, ticket);
        var actions = history.Select(h => h.Action).ToList();

        Assert.Contains(TicketHistoryAction.CommentAdded, actions);
        Assert.Contains(TicketHistoryAction.InternalCommentAdded, actions);
    }

    [Fact]
    public async Task Chamado_fechado_nao_aceita_comentario()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);
        await ChangeStatusAsync(world.Manager, ticket, TicketStatus.Closed);

        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Tarde demais."));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Chamado_resolvido_ainda_aceita_comentario()
    {
        // É onde o solicitante diz que o problema voltou.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);

        var response = await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("O erro voltou hoje."));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ----- Marco do SLA de resposta -----

    [Fact]
    public async Task O_primeiro_comentario_publico_da_equipe_encerra_o_sla_de_resposta()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        Assert.Null((await GetAsync(world.Manager, ticket)).FirstRespondedAt);

        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Estamos verificando."));

        Assert.NotNull((await GetAsync(world.Manager, ticket)).FirstRespondedAt);
    }

    [Fact]
    public async Task Comentario_interno_nao_encerra_o_sla_de_resposta()
    {
        // Um SLA de resposta que se fecha com uma observação que o solicitante nunca vê
        // mediria a conversa da equipe consigo mesma.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest("Verificar o AD.", IsInternal: true));

        Assert.Null((await GetAsync(world.Manager, ticket)).FirstRespondedAt);
    }

    [Fact]
    public async Task Comentario_do_solicitante_nao_encerra_o_sla_de_resposta()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Alguma novidade?"));

        Assert.Null((await GetAsync(world.Manager, ticket)).FirstRespondedAt);
    }

    [Fact]
    public async Task O_marco_de_resposta_nao_e_reescrito_por_comentarios_seguintes()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Primeira resposta."));

        var first = (await GetAsync(world.Manager, ticket)).FirstRespondedAt;

        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Segunda resposta."));

        Assert.Equal(first, (await GetAsync(world.Manager, ticket)).FirstRespondedAt);
    }

    // ----- Mudança de status -----

    [Fact]
    public async Task Tecnico_move_o_chamado_pelo_grafo()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var inProgress = await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        Assert.Equal(TicketStatus.InProgress, inProgress.Status);

        var resolved = await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);
        Assert.Equal(TicketStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);
    }

    [Fact]
    public async Task Transicao_fora_do_grafo_e_recusada()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        // Aberto não vai direto para Resolvido.
        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/status", new ChangeStatusRequest(TicketStatus.Resolved));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Solicitante_cancela_o_proprio_chamado_mas_nao_o_atende()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var forbidden = await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{ticket}/status", new ChangeStatusRequest(TicketStatus.InProgress));

        Assert.Equal(HttpStatusCode.Conflict, forbidden.StatusCode);

        var cancelled = await ChangeStatusAsync(world.Requester, ticket, TicketStatus.Cancelled);

        Assert.Equal(TicketStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Solicitante_fecha_chamado_resolvido_confirmando_a_solucao()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);

        var closed = await ChangeStatusAsync(world.Requester, ticket, TicketStatus.Closed);

        Assert.Equal(TicketStatus.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAt);
    }

    [Fact]
    public async Task Solicitante_nao_muda_status_de_chamado_de_terceiro()
    {
        var world = await SetUpAsync();
        var ofSomeoneElse = await OpenTicketAsync(world, client: world.OtherRequester);

        var response = await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{ofSomeoneElse}/status", new ChangeStatusRequest(TicketStatus.Cancelled));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Chamado_fechado_nao_se_move_mais()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);
        await ChangeStatusAsync(world.Manager, ticket, TicketStatus.Closed);

        foreach (var target in Enum.GetValues<TicketStatus>())
        {
            var response = await world.Manager.PostAsJsonAsync(
                $"/api/tickets/{ticket}/status", new ChangeStatusRequest(target));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [Fact]
    public async Task Mudanca_de_status_gera_historico_com_valor_anterior_e_novo()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);

        var history = await HistoryAsync(world.Manager, ticket);
        var entry = Assert.Single(history, h => h.Action == TicketHistoryAction.StatusChanged);

        Assert.Equal(nameof(TicketStatus.Open), entry.PreviousValue);
        Assert.Equal(nameof(TicketStatus.InProgress), entry.NewValue);
        Assert.Equal("Usuário Technician", entry.ChangedByName);
    }

    // ----- Pausa do SLA -----

    [Fact]
    public async Task Aguardando_usuario_pausa_o_sla_de_resolucao()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        var paused = await ChangeStatusAsync(world.Technician, ticket, TicketStatus.WaitingOnRequester);

        Assert.True(paused.SlaPaused);
    }

    [Fact]
    public async Task Sair_de_aguardando_usuario_retoma_e_empurra_o_prazo()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        var paused = await ChangeStatusAsync(world.Technician, ticket, TicketStatus.WaitingOnRequester);

        // Avança o instante da pausa para trás, simulando tempo em espera sem esperar
        // de verdade. Duas horas úteis: a janela de expediente é ampla o bastante para o
        // instante deslocado continuar dentro dela na maior parte do dia.
        await using var db = Fixture.CreateContext();
        var pausedAt = await db.Tickets
            .Where(t => t.Id == ticket)
            .Select(t => t.SlaPausedAt)
            .SingleAsync();

        await db.Tickets
            .Where(t => t.Id == ticket)
            .ExecuteUpdateAsync(t => t.SetProperty(
                x => x.SlaPausedAt, pausedAt!.Value.AddHours(-2)));

        var resumed = await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);

        Assert.False(resumed.SlaPaused);

        // O prazo de resolução andou para frente, e o de resposta ficou onde estava —
        // esperar o solicitante não é desculpa para não ter respondido (README, 8.2).
        Assert.True(resumed.SlaResolutionDueAt > paused.SlaResolutionDueAt);
        Assert.Equal(paused.SlaResponseDueAt, resumed.SlaResponseDueAt);
        Assert.True(resumed.SlaPausedBusinessMinutes > 0);
    }

    [Fact]
    public async Task Resolver_um_chamado_pausado_encerra_a_pausa()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.WaitingOnRequester);

        var resolved = await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);

        Assert.False(resolved.SlaPaused);
        Assert.NotNull(resolved.ResolvedAt);
    }

    [Fact]
    public async Task Reabrir_chamado_resolvido_limpa_a_data_de_resolucao()
    {
        // Deixá-la preenchida faria o chamado nunca mais aparecer como vencido, por mais
        // que se arrastasse. A data da primeira resolução permanece no histórico.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);

        var reopened = await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);

        Assert.Null(reopened.ResolvedAt);

        var history = await HistoryAsync(world.Manager, ticket);
        Assert.Contains(history, h => h.Action == TicketHistoryAction.Resolved);
    }

    // ----- Reclassificação: prioridade e categoria -----

    [Fact]
    public async Task Tecnico_troca_a_prioridade_e_o_prazo_e_recalculado()
    {
        // Média: 8 horas úteis para resposta, 30 para resolução. Crítica: 1 e 4. Promover
        // a prioridade tem de apertar o prazo — se não apertar, a prioridade é enfeite e o
        // indicador de vencidos mede a prioridade errada.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world, priority: TicketPriority.Medium);

        var before = await GetAsync(world.Technician, ticket);
        var after = await ReclassifyAsync(world.Technician, ticket, priority: TicketPriority.Critical);

        Assert.Equal(TicketPriority.Critical, after.Priority);
        Assert.True(after.SlaResponseDueAt < before.SlaResponseDueAt);
        Assert.True(after.SlaResolutionDueAt < before.SlaResolutionDueAt);

        // A conta recomeça da abertura, não da reclassificação: uma hora útil depois de
        // aberto, e não uma hora depois de alguém ter mudado o campo.
        Assert.True(after.SlaResponseDueAt >= after.CreatedAt.AddHours(1));

        var history = await HistoryAsync(world.Technician, ticket);
        var entry = Assert.Single(history, h => h.Action == TicketHistoryAction.PriorityChanged);
        Assert.Equal(nameof(TicketPriority.Medium), entry.PreviousValue);
        Assert.Equal(nameof(TicketPriority.Critical), entry.NewValue);
    }

    [Fact]
    public async Task Rebaixar_a_prioridade_afrouxa_o_prazo()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world, priority: TicketPriority.Critical);

        var before = await GetAsync(world.Technician, ticket);
        var after = await ReclassifyAsync(world.Technician, ticket, priority: TicketPriority.Low);

        Assert.True(after.SlaResolutionDueAt > before.SlaResolutionDueAt);
    }

    [Fact]
    public async Task Reclassificar_nao_reescreve_marco_de_resposta_ja_cumprido()
    {
        // O prazo de resposta de um chamado já respondido não é recalculado: mexer nele
        // transformaria retroativamente um atendimento pontual em atrasado, ou o contrário.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world, priority: TicketPriority.Low);

        await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Estou verificando."));

        var before = await GetAsync(world.Technician, ticket);
        Assert.NotNull(before.FirstRespondedAt);

        var after = await ReclassifyAsync(world.Technician, ticket, priority: TicketPriority.Critical);

        Assert.Equal(before.SlaResponseDueAt, after.SlaResponseDueAt);

        // A resolução, que ainda não aconteceu, é recalculada normalmente.
        Assert.True(after.SlaResolutionDueAt < before.SlaResolutionDueAt);
    }

    [Fact]
    public async Task Reclassificar_chamado_resolvido_nao_mexe_no_prazo_de_resolucao()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world, priority: TicketPriority.Low);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);

        var before = await GetAsync(world.Technician, ticket);
        var after = await ReclassifyAsync(world.Technician, ticket, priority: TicketPriority.Critical);

        Assert.Equal(TicketPriority.Critical, after.Priority);
        Assert.Equal(before.SlaResolutionDueAt, after.SlaResolutionDueAt);
    }

    [Fact]
    public async Task Reclassificar_preserva_o_tempo_que_o_chamado_passou_em_espera()
    {
        // A pausa acumulada é um direito adquirido do chamado: o tempo em que ele esperou
        // o solicitante não some porque alguém trocou a prioridade depois.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world, priority: TicketPriority.Medium);

        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.WaitingOnRequester);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);

        var before = await GetAsync(world.Technician, ticket);
        var after = await ReclassifyAsync(world.Technician, ticket, priority: TicketPriority.High);

        // A pausa acumulada sobrevive à reclassificação, e o prazo não fica menor que o de
        // um chamado da mesma prioridade aberto agora, que nunca esperou ninguém.
        var semPausa = await OpenTicketAsync(world, title: "Sem pausa", priority: TicketPriority.High);
        var outro = await GetAsync(world.Technician, semPausa);

        Assert.Equal(before.SlaPausedBusinessMinutes, after.SlaPausedBusinessMinutes);
        Assert.True(
            after.SlaResolutionDueAt >= outro.SlaResolutionDueAt,
            "o chamado que ficou em espera nao pode terminar com prazo mais curto que um aberto agora");
    }

    [Fact]
    public async Task Gestor_troca_a_categoria_sem_mexer_no_prazo()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        var before = await GetAsync(world.Manager, ticket);

        Guid outraCategoria;
        await using (var db = Fixture.CreateContext())
        {
            outraCategoria = await db.Categories
                .Where(c => c.Id != world.CategoryId && c.IsActive)
                .Select(c => c.Id)
                .FirstAsync();
        }

        var after = await ReclassifyAsync(world.Manager, ticket, categoryId: outraCategoria);

        Assert.Equal(outraCategoria, after.CategoryId);
        Assert.Equal(before.SlaResponseDueAt, after.SlaResponseDueAt);
        Assert.Equal(before.SlaResolutionDueAt, after.SlaResolutionDueAt);

        var history = await HistoryAsync(world.Manager, ticket);
        Assert.Contains(history, h => h.Action == TicketHistoryAction.CategoryChanged);
    }

    [Fact]
    public async Task Solicitante_nao_reclassifica_o_proprio_chamado()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{ticket}/classification",
            new ChangeClassificationRequest(TicketPriority.Critical, null));

        // Barrado pela política de rota, antes de chegar ao serviço.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var unchanged = await GetAsync(world.Manager, ticket);
        Assert.Equal(TicketPriority.Medium, unchanged.Priority);
    }

    [Fact]
    public async Task Reclassificar_chamado_fechado_e_recusado()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Closed);

        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/classification",
            new ChangeClassificationRequest(TicketPriority.Critical, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reclassificar_com_categoria_inexistente_e_recusado()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/classification",
            new ChangeClassificationRequest(null, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reclassificacao_vazia_e_recusada()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/classification",
            new ChangeClassificationRequest(null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tecnico_reclassifica_chamado_de_outro_tecnico()
    {
        // Triagem é da equipe, e não de quem por acaso assumiu o chamado: prioridade
        // errada na fila do colega tem de poder ser corrigida por quem notar.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.OtherTechnicianId);

        var after = await ReclassifyAsync(
            world.Technician, ticket, priority: TicketPriority.Critical);

        Assert.Equal(TicketPriority.Critical, after.Priority);
    }

    [Fact]
    public async Task Solicitante_nao_reclassifica_chamado_de_terceiro()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await world.OtherRequester.PostAsJsonAsync(
            $"/api/tickets/{ticket}/classification",
            new ChangeClassificationRequest(TicketPriority.Critical, null));

        // Barrado pela política de rota, que só aceita equipe.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<TicketDetail> ReclassifyAsync(
        HttpClient client, Guid id, TicketPriority? priority = null, Guid? categoryId = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{id}/classification",
            new ChangeClassificationRequest(priority, categoryId));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<TicketDetail>())!;
    }

    // ----- Atribuição -----

    [Fact]
    public async Task Tecnico_assume_o_chamado_para_si()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var assigned = await AssignAsync(world.Technician, ticket, world.TechnicianId);

        Assert.Equal(world.TechnicianId, assigned.AssignedTechnicianId);

        // Atribuir não muda status: são duas ações explícitas e separadas.
        Assert.Equal(TicketStatus.Open, assigned.Status);
    }

    [Fact]
    public async Task Tecnico_encaminha_chamado_para_outro_tecnico_e_continua_vendo()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var forwarded = await AssignAsync(world.Technician, ticket, world.OtherTechnicianId);

        Assert.Equal(world.OtherTechnicianId, forwarded.AssignedTechnicianId);

        // Encaminhar não tira o chamado da vista de quem encaminhou. Enquanto técnico via
        // só os sem responsável e os seus, esta chamada respondia 204 e a interface
        // precisava devolver a pessoa para a lista.
        Assert.Equal(
            HttpStatusCode.OK,
            (await world.Technician.GetAsync($"/api/tickets/{ticket}")).StatusCode);

        var received = await GetAsync(world.OtherTechnician, ticket);
        Assert.Equal(world.OtherTechnicianId, received.AssignedTechnicianId);
    }

    [Fact]
    public async Task Tecnico_devolve_para_a_fila_o_chamado_que_e_seu()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await AssignAsync(world.Technician, ticket, world.TechnicianId);
        var unassigned = await AssignAsync(world.Technician, ticket, null);

        Assert.Null(unassigned.AssignedTechnicianId);
    }

    [Fact]
    public async Task Tecnico_assume_para_si_chamado_de_outro_tecnico()
    {
        // O caso de quem volta de férias e precisa puxar de volta o próprio chamado, ou de
        // quem assume a fila de um colega ausente. Antes dependia do gestor.
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await AssignAsync(world.Manager, ticket, world.OtherTechnicianId);

        var taken = await AssignAsync(world.Technician, ticket, world.TechnicianId);

        Assert.Equal(world.TechnicianId, taken.AssignedTechnicianId);
    }

    [Fact]
    public async Task Gestor_atribui_a_qualquer_tecnico()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var assigned = await AssignAsync(world.Manager, ticket, world.OtherTechnicianId);

        Assert.Equal(world.OtherTechnicianId, assigned.AssignedTechnicianId);
    }

    [Fact]
    public async Task Gestor_remove_o_responsavel()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await AssignAsync(world.Manager, ticket, world.TechnicianId);
        var unassigned = await AssignAsync(world.Manager, ticket, null);

        Assert.Null(unassigned.AssignedTechnicianId);

        var history = await HistoryAsync(world.Manager, ticket);
        Assert.Contains(history, h => h.Action == TicketHistoryAction.TechnicianUnassigned);
    }

    [Fact]
    public async Task Solicitante_nao_atribui_responsavel()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await world.Requester.PostAsJsonAsync(
            $"/api/tickets/{ticket}/assignment", new AssignRequest(world.TechnicianId));

        // Barrado pela política de rota, antes mesmo de chegar ao serviço.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Atribuir_a_um_solicitante_e_recusado()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await world.Manager.PostAsJsonAsync(
            $"/api/tickets/{ticket}/assignment", new AssignRequest(world.OtherRequesterId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Atribuicao_gera_historico()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await AssignAsync(world.Manager, ticket, world.TechnicianId);

        var history = await HistoryAsync(world.Manager, ticket);
        var entry = Assert.Single(history, h => h.Action == TicketHistoryAction.TechnicianAssigned);

        Assert.Equal(world.TechnicianId.ToString(), entry.NewValue);
    }

    [Fact]
    public async Task Lista_de_responsaveis_traz_apenas_equipe_ativa()
    {
        var world = await SetUpAsync();

        var response = await world.Manager.GetAsync("/api/staff");
        var staff = await response.Content.ReadJsonAsync<List<StaffOption>>();

        Assert.NotNull(staff);
        Assert.All(staff, s => Assert.True(s.Role is UserRole.Technician or UserRole.Manager));
        Assert.DoesNotContain(staff, s => s.Id == world.RequesterId);
    }

    [Fact]
    public async Task Solicitante_nao_acessa_a_lista_de_responsaveis()
    {
        var world = await SetUpAsync();

        var response = await world.Requester.GetAsync("/api/staff");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ----- Histórico -----

    [Fact]
    public async Task Solicitante_le_o_historico_do_proprio_chamado()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var history = await HistoryAsync(world.Requester, ticket);

        Assert.Contains(history, h => h.Action == TicketHistoryAction.Created);
    }

    [Fact]
    public async Task Solicitante_nao_le_historico_de_chamado_de_terceiro()
    {
        var world = await SetUpAsync();
        var ofSomeoneElse = await OpenTicketAsync(world, client: world.OtherRequester);

        var history = await HistoryAsync(world.Requester, ofSomeoneElse);

        Assert.Empty(history);
    }
}
