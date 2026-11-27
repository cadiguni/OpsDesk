using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Application.Common;
using OpsDesk.Application.Inbound;
using OpsDesk.Application.Notifications;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Chamados por e-mail e caixa de SPAM, pelos critérios de aceite de
/// docs/integracao-email.md, seção 8 — e pelos casos de segurança que o canal abre: quem
/// não é o solicitante não comenta no chamado alheio só porque pôs o código no assunto.
/// </summary>
[Collection(PostgresCollection.Name)]
public class InboundEmailTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    private const string Mailbox = "suporte@empresa.com";

    // ----- Critérios de aceite -----

    [Fact]
    public async Task Mensagem_na_caixa_monitorada_vira_chamado_do_remetente()
    {
        var world = await SetUpWithMailboxAsync();
        var requesterEmail = await EmailOfAsync(world.RequesterId);

        var message = Fixture.Api.Mailbox.Deliver(Mailbox, requesterEmail, "Impressora travada", "A impressora do 2º andar não imprime.");
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        var ticket = await db.Tickets.SingleAsync(t => t.Source == TicketSource.Email);
        Assert.Equal(world.RequesterId, ticket.RequesterId);
        Assert.Equal("Impressora travada", ticket.Title);
        Assert.Equal(message.MessageId, ticket.ExternalId);
        Assert.Equal(TicketPriority.Medium, ticket.Priority);

        var inbound = await db.InboundEmails.SingleAsync();
        Assert.Equal(InboundEmailStatus.Processed, inbound.Status);
        Assert.Equal(ticket.Id, inbound.TicketId);
        Assert.Contains(message.ProviderId, Fixture.Api.Mailbox.MarkedAsRead);
    }

    [Fact]
    public async Task Remetente_desconhecido_vira_usuario_sem_senha_e_dono_do_chamado()
    {
        await SetUpWithMailboxAsync();

        Fixture.Api.Mailbox.Deliver(Mailbox, "Nova.Pessoa@Empresa.com", "Acesso à pasta", "Preciso de acesso.", fromName: "Nova Pessoa");
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        var user = await db.Users.SingleAsync(u => u.Email == "nova.pessoa@empresa.com");
        Assert.Null(user.PasswordHash);
        Assert.Equal(UserRole.Requester, user.Role);
        Assert.Equal("Nova Pessoa", user.Name);
        Assert.True(await db.Tickets.AnyAsync(t => t.RequesterId == user.Id));
    }

    [Fact]
    public async Task Resposta_com_o_codigo_vira_comentario_no_chamado_certo_e_retoma_o_atendimento()
    {
        var world = await SetUpWithMailboxAsync();
        var ticket = await OpenTicketAsync(world);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.WaitingOnRequester);
        var code = (await GetAsync(world.Manager, ticket)).Code;
        var requesterEmail = await EmailOfAsync(world.RequesterId);
        var emailsBefore = await EmailsToAsync(requesterEmail);

        Fixture.Api.Mailbox.Deliver(Mailbox, requesterEmail, $"RE: [{code}] Chamado aguardando sua resposta: VPN",
            $"Segue o print que você pediu.\n\n—— {ReplyTextExtractor.Marker} ——\nOlá...");
        await SyncAsync();

        var comment = Assert.Single(await CommentsAsync(world.Manager, ticket));
        Assert.Equal("Segue o print que você pediu.", comment.Content);
        Assert.Equal(world.RequesterId, comment.AuthorId);
        Assert.Equal(TicketStatus.InProgress, (await GetAsync(world.Manager, ticket)).Status);

        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.Tickets.CountAsync());

        // O histórico tem o autor de verdade, e a pessoa não recebe e-mail da própria
        // resposta — nem da volta para "em atendimento" que a resposta causou.
        var history = await HistoryAsync(world.Manager, ticket);
        Assert.Contains(history, h => h.Action == TicketHistoryAction.CommentAdded && h.ChangedById == world.RequesterId);
        Assert.Equal(emailsBefore, await EmailsToAsync(requesterEmail));
    }

    [Fact]
    public async Task Resposta_de_ferias_nao_cria_chamado()
    {
        await SetUpWithMailboxAsync();

        Fixture.Api.Mailbox.Deliver(Mailbox, "fulano@empresa.com", "Ausência temporária", "Estou de férias até dia 10.",
            headers: new Dictionary<string, string> { ["auto-submitted"] = "auto-replied" });
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        Assert.False(await db.Tickets.AnyAsync());
        var held = await db.InboundEmails.SingleAsync();
        Assert.Equal(InboundEmailStatus.Spam, held.Status);
        Assert.Contains("Auto-Submitted", held.SpamReason);
    }

    [Fact]
    public async Task Mesma_mensagem_reentregue_nao_cria_dois_chamados()
    {
        await SetUpWithMailboxAsync();

        Fixture.Api.Mailbox.Deliver(Mailbox, "fulano@empresa.com", "Wi-Fi caiu", "Sem rede no 3º andar.", messageId: "<abc@empresa.com>");
        await SyncAsync();
        Fixture.Api.Mailbox.Deliver(Mailbox, "fulano@empresa.com", "Wi-Fi caiu", "Sem rede no 3º andar.", messageId: "<abc@empresa.com>");
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.Tickets.CountAsync());
        Assert.Equal(1, await db.InboundEmails.CountAsync(e => e.Status == InboundEmailStatus.Duplicate));
    }

    [Fact]
    public async Task Mensagem_retida_vira_chamado_em_um_clique_e_registra_quem_revisou()
    {
        var world = await SetUpWithMailboxAsync();
        await BlockAsync(SpamRuleType.SenderDomain, "parceiro.com");

        Fixture.Api.Mailbox.Deliver(Mailbox, "ana@parceiro.com", "Acesso ao portal de compras", "Não consigo entrar.");
        await SyncAsync();
        var held = await SingleInboundAsync(world.Technician, InboundEmailStatus.Spam);

        var response = await world.Technician.PostAsJsonAsync($"/api/inbound/{held.Id}/review", new ReviewSpamRequest(SpamReviewAction.NotSpam), TestJson.Options);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reviewed = await response.Content.ReadJsonAsync<InboundEmailItem>();
        Assert.Equal(InboundEmailStatus.Processed, reviewed!.Status);
        Assert.NotNull(reviewed.TicketCode);

        await using var db = Fixture.CreateContext();
        var stored = await db.InboundEmails.SingleAsync();
        Assert.Equal(world.TechnicianId, stored.ReviewedById);
    }

    [Fact]
    public async Task Confirmar_spam_impede_o_remetente_de_gerar_chamado_de_novo()
    {
        var world = await SetUpWithMailboxAsync();
        await BlockAsync(SpamRuleType.SubjectContains, "promoção");

        Fixture.Api.Mailbox.Deliver(Mailbox, "vendas@spam.example", "Promoção imperdível", "Compre já.");
        await SyncAsync();
        var held = await SingleInboundAsync(world.Technician, InboundEmailStatus.Spam);

        await world.Technician.PostAsJsonAsync($"/api/inbound/{held.Id}/review", new ReviewSpamRequest(SpamReviewAction.ConfirmAndBlockSender), TestJson.Options);

        Fixture.Api.Mailbox.Deliver(Mailbox, "vendas@spam.example", "Oi, tudo bem?", "Uma oferta só para você.");
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        Assert.False(await db.Tickets.AnyAsync());
        var second = await db.InboundEmails.OrderByDescending(e => e.CreatedAt).FirstAsync();
        Assert.Equal(InboundEmailStatus.Spam, second.Status);
        Assert.Contains("vendas@spam.example", second.SpamReason);
    }

    [Fact]
    public async Task Nota_interna_em_chamado_aberto_por_email_nao_vai_por_email()
    {
        var world = await SetUpWithMailboxAsync();
        Fixture.Api.Mailbox.Deliver(Mailbox, await EmailOfAsync(world.RequesterId), "VPN", "Não conecta.");
        await SyncAsync();

        Guid ticket;
        await using (var db = Fixture.CreateContext())
        {
            ticket = (await db.Tickets.SingleAsync()).Id;
        }

        await world.Technician.PostAsJsonAsync($"/api/tickets/{ticket}/comments", new AddCommentRequest("Certificado expirado, ver com redes.", IsInternal: true));

        await using var after = Fixture.CreateContext();
        Assert.DoesNotContain(await after.OutboundEmails.Select(e => e.HtmlBody).ToListAsync(), body => body.Contains("Certificado expirado"));
    }

    // ----- Segurança do canal -----

    [Fact]
    public async Task Codigo_no_assunto_nao_deixa_estranho_comentar_no_chamado_alheio()
    {
        var world = await SetUpWithMailboxAsync();
        var ticket = await OpenTicketAsync(world);
        var code = (await GetAsync(world.Manager, ticket)).Code;

        Fixture.Api.Mailbox.Deliver(Mailbox, await EmailOfAsync(world.OtherRequesterId), $"RE: [{code}] VPN", "Mudem a prioridade para crítica.");
        await SyncAsync();

        Assert.Empty(await CommentsAsync(world.Manager, ticket));

        await using var db = Fixture.CreateContext();
        var created = await db.Tickets.SingleAsync(t => t.Source == TicketSource.Email);
        Assert.Equal(world.OtherRequesterId, created.RequesterId);
    }

    [Fact]
    public async Task Resposta_a_chamado_fechado_fora_da_janela_vira_chamado_novo_citando_o_antigo()
    {
        var world = await SetUpWithMailboxAsync();
        var ticket = await OpenTicketAsync(world);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);
        await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Closed);
        var code = (await GetAsync(world.Manager, ticket)).Code;

        await using (var db = Fixture.CreateContext())
        {
            await db.Tickets.Where(t => t.Id == ticket)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.ClosedAt, DateTimeOffset.UtcNow.AddDays(-30)));
        }

        Fixture.Api.Mailbox.Deliver(Mailbox, await EmailOfAsync(world.RequesterId), $"RE: [{code}] VPN", "Voltou a acontecer.");
        await SyncAsync();

        await using var after = Fixture.CreateContext();
        var created = await after.Tickets.SingleAsync(t => t.Source == TicketSource.Email);
        Assert.Contains(code, created.Description);
        Assert.Equal("VPN", created.Title);
        Assert.Equal(TicketStatus.Closed, (await after.Tickets.SingleAsync(t => t.Id == ticket)).Status);
    }

    [Fact]
    public async Task Remetente_com_conta_desativada_vai_para_o_spam()
    {
        var world = await SetUpWithMailboxAsync();
        await world.Manager.PostAsJsonAsync($"/api/admin/users/{world.OtherRequesterId}/activation", new Application.Users.ChangeActivationRequest(false));

        Fixture.Api.Mailbox.Deliver(Mailbox, await EmailOfAsync(world.OtherRequesterId), "Ajuda", "Texto.");
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        Assert.False(await db.Tickets.AnyAsync());
        Assert.Equal("Remetente com conta desativada", (await db.InboundEmails.SingleAsync()).SpamReason);
    }

    [Fact]
    public async Task Solicitante_nao_acessa_a_caixa_de_spam_e_tecnico_nao_exclui()
    {
        var world = await SetUpWithMailboxAsync();
        Fixture.Api.Mailbox.Deliver(Mailbox, "noreply@algo.com", "x", "y");
        await SyncAsync();
        var held = await SingleInboundAsync(world.Technician, InboundEmailStatus.Spam);

        Assert.Equal(HttpStatusCode.Forbidden, (await world.Requester.GetAsync("/api/inbound")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await world.Technician.DeleteAsync($"/api/inbound/{held.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await world.Manager.DeleteAsync($"/api/inbound/{held.Id}")).StatusCode);
    }

    // ----- Falhas e reprocessamento -----

    [Fact]
    public async Task Falha_fica_registrada_e_reprocessar_resolve()
    {
        var world = await SetUpWithMailboxAsync();

        await using (var db = Fixture.CreateContext())
        {
            await db.SlaPolicies.ExecuteUpdateAsync(set => set.SetProperty(p => p.IsActive, false));
        }

        Fixture.Api.Mailbox.Deliver(Mailbox, "fulano@empresa.com", "Teclado", "Teclas falhando.");
        await SyncAsync();

        var failed = await SingleInboundAsync(world.Technician, InboundEmailStatus.Failed);
        Assert.Contains("política de SLA", failed.ProcessingError);

        await using (var db = Fixture.CreateContext())
        {
            await db.SlaPolicies.ExecuteUpdateAsync(set => set.SetProperty(p => p.IsActive, true));
        }

        var response = await world.Technician.PostAsync($"/api/inbound/{failed.Id}/reprocess", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(InboundEmailStatus.Processed, (await response.Content.ReadJsonAsync<InboundEmailItem>())!.Status);
    }

    [Fact]
    public async Task Falha_de_leitura_aparece_na_caixa_e_nao_derruba_a_rodada()
    {
        var world = await SetUpWithMailboxAsync();
        Fixture.Api.Mailbox.FailWith = "O Microsoft Graph recusou a leitura (403): Access denied.";

        await SyncAsync();

        var accounts = await world.Manager.GetFromJsonAsync<List<EmailAccountItem>>("/api/admin/settings/email/accounts", TestJson.Options);
        Assert.Contains("Access denied", Assert.Single(accounts!).LastError);
    }

    // ----- E-mail de saída -----

    [Fact]
    public async Task Com_caixa_monitorada_o_email_convida_a_responder_e_leva_a_marca()
    {
        var world = await SetUpWithMailboxAsync();
        var ticket = await OpenTicketAsync(world);

        await world.Technician.PostAsJsonAsync($"/api/tickets/{ticket}/comments", new AddCommentRequest("Pode testar?"));

        await using var db = Fixture.CreateContext();
        var body = (await db.OutboundEmails.SingleAsync()).HtmlBody;
        Assert.Contains(ReplyTextExtractor.Marker, body);
        Assert.DoesNotContain("não são lidas", body);
    }

    [Fact]
    public async Task Retencao_apaga_spam_antigo()
    {
        await SetUpWithMailboxAsync();
        Fixture.Api.Mailbox.Deliver(Mailbox, "noreply@algo.com", "x", "y");
        await SyncAsync();

        await using (var db = Fixture.CreateContext())
        {
            await db.InboundEmails.ExecuteUpdateAsync(set => set.SetProperty(e => e.ReceivedAt, DateTimeOffset.UtcNow.AddDays(-91)));
        }

        using (var scope = Fixture.Api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<InboundRetentionService>().RunAsync();
        }

        await using var after = Fixture.CreateContext();
        Assert.False(await after.InboundEmails.AnyAsync());
    }

    // ----- Receber sem enviar -----

    /// <summary>
    /// Receber chamado por e-mail e avisar o solicitante por e-mail são decisões separadas.
    /// Com o envio desligado a caixa continua sendo lida, e nada entra na fila de saída —
    /// nem quando a equipe responde o chamado que veio por e-mail.
    /// </summary>
    [Fact]
    public async Task Caixa_e_lida_com_o_envio_desligado_e_nada_entra_na_fila()
    {
        var world = await SetUpWithMailboxAsync(sendingEnabled: false);
        var requesterEmail = await EmailOfAsync(world.RequesterId);

        Fixture.Api.Mailbox.Deliver(Mailbox, requesterEmail, "Impressora travada", "Não imprime.");
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        var ticket = await db.Tickets.SingleAsync(t => t.Source == TicketSource.Email);

        (await world.Technician.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new AddCommentRequest("Pode testar?")))
            .EnsureSuccessStatusCode();

        await using var after = Fixture.CreateContext();
        Assert.False(await after.OutboundEmails.AnyAsync());
    }

    [Fact]
    public async Task Sem_credenciais_do_aplicativo_a_caixa_nao_e_lida()
    {
        Fixture.Api.Email.Reset();
        Fixture.Api.Mailbox.Reset();

        var world = await SetUpAsync();

        (await world.Manager.PostAsJsonAsync("/api/admin/settings/email/accounts",
            new SaveEmailAccountRequest("Suporte TI", Mailbox, null, TicketPriority.Medium, false, true), TestJson.Options)).EnsureSuccessStatusCode();

        Fixture.Api.Mailbox.Deliver(Mailbox, "alguem@empresa.com", "Teste", "Corpo.");
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        Assert.False(await db.InboundEmails.AnyAsync());
        Assert.Empty(Fixture.Api.Mailbox.MarkedAsRead);
    }

    // ----- Atalhos -----

    private async Task<World> SetUpWithMailboxAsync(bool sendingEnabled = true)
    {
        Fixture.Api.Email.Reset();
        Fixture.Api.Mailbox.Reset();

        var world = await SetUpAsync();

        (await world.Manager.PutAsJsonAsync("/api/admin/settings/email", new UpdateEmailSettingsRequest(
            sendingEnabled, Mailbox, "Suporte TI", "empresa.onmicrosoft.com",
            "11111111-2222-3333-4444-555555555555", "segredo", null, "https://opsdesk.empresa.com"))).EnsureSuccessStatusCode();

        (await world.Manager.PostAsJsonAsync("/api/admin/settings/email/accounts",
            new SaveEmailAccountRequest("Suporte TI", Mailbox, null, TicketPriority.Medium, false, true), TestJson.Options)).EnsureSuccessStatusCode();

        return world;
    }

    private async Task SyncAsync()
    {
        using var scope = Fixture.Api.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<MailboxSyncService>().SyncAsync();
    }

    private async Task BlockAsync(SpamRuleType type, string pattern)
    {
        await using var db = Fixture.CreateContext();

        db.SpamRules.Add(new SpamRule { Type = type, Pattern = pattern, Action = SpamRuleAction.Block });
        await db.SaveChangesAsync();
    }

    private static async Task<InboundEmailItem> SingleInboundAsync(HttpClient staff, InboundEmailStatus status)
    {
        var page = await staff.GetFromJsonAsync<PagedResult<InboundEmailItem>>($"/api/inbound?status={status}", TestJson.Options);

        return Assert.Single(page!.Items);
    }

    private async Task<int> EmailsToAsync(string address)
    {
        await using var db = Fixture.CreateContext();

        return await db.OutboundEmails.CountAsync(e => e.ToAddress == address);
    }

    private async Task<string> EmailOfAsync(Guid userId)
    {
        await using var db = Fixture.CreateContext();

        return await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync();
    }
}
