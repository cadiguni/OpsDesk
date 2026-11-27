using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Application.Attachments;
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

    // ----- Anexos da mensagem -----

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];

    /// <summary>
    /// O print colado no corpo e o PDF anexado entram no chamado, visíveis ao solicitante;
    /// a marca <c>[cid:…]</c> do texto vira o nome do arquivo. O executável fica de fora,
    /// pela mesma lista de tipos do portal, sem impedir o chamado.
    /// </summary>
    [Fact]
    public async Task Anexos_e_imagens_do_corpo_entram_no_chamado_novo()
    {
        var world = await SetUpWithMailboxAsync();

        Fixture.Api.Mailbox.Deliver(Mailbox, await EmailOfAsync(world.RequesterId), "Erro no sistema",
            "Veja o erro:\n\n[cid:ABC-123]\n\nAtenciosamente,\n[cid:nao-trazida]",
            attachments:
            [
                new FetchedAttachment("print.png", "image/png", Png, IsInline: true, ContentId: "<abc-123>"),
                new FetchedAttachment("relatorio.pdf", "application/pdf", [1, 2, 3], IsInline: false, ContentId: null),
                new FetchedAttachment("virus.exe", "application/x-msdownload", [1, 2], IsInline: false, ContentId: null)
            ]);
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        var ticket = await db.Tickets.SingleAsync(t => t.Source == TicketSource.Email);

        Assert.Contains("[imagem: print.png]", ticket.Description);
        Assert.Contains("[imagem]", ticket.Description);
        Assert.DoesNotContain("[cid:", ticket.Description);

        var attachments = await ListAttachmentsAsync(world.Requester, ticket.Id);
        Assert.Equal(["print.png", "relatorio.pdf"], attachments.Select(a => a.FileName).Order().ToArray());
        Assert.All(attachments, a =>
        {
            Assert.Null(a.CommentId);
            Assert.False(a.IsInternal);
            Assert.Equal(world.RequesterId, a.UploadedById);
        });
        Assert.True(attachments.Single(a => a.FileName == "print.png").IsImage);
    }

    /// <summary>
    /// A imagem da assinatura volta em toda resposta. No chamado que já a tem, ela não entra
    /// de novo; o print novo da resposta entra, no comentário.
    /// </summary>
    [Fact]
    public async Task Resposta_traz_o_print_novo_e_nao_repete_a_imagem_da_assinatura()
    {
        var world = await SetUpWithMailboxAsync();
        var from = await EmailOfAsync(world.RequesterId);
        var signature = new FetchedAttachment("logo.png", "image/png", Png, IsInline: true, ContentId: "logo");

        Fixture.Api.Mailbox.Deliver(Mailbox, from, "VPN", "Não conecta.\n[cid:logo]", attachments: [signature]);
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        var ticket = await db.Tickets.SingleAsync(t => t.Source == TicketSource.Email);

        Fixture.Api.Mailbox.Deliver(Mailbox, from, $"RE: [{ticket.Code}] VPN", "Segue o print.\n[cid:tela]\n[cid:logo]",
            attachments:
            [
                new FetchedAttachment("tela.png", "image/png", [0x89, 0x50, 9, 9, 9], IsInline: true, ContentId: "tela"),
                signature
            ]);
        await SyncAsync();

        var attachments = await ListAttachmentsAsync(world.Requester, ticket.Id);
        Assert.Equal(2, attachments.Count);
        Assert.Null(attachments.Single(a => a.FileName == "logo.png").CommentId);

        var comment = await db.TicketComments.SingleAsync(c => c.TicketId == ticket.Id);
        Assert.Equal(comment.Id, attachments.Single(a => a.FileName == "tela.png").CommentId);
        Assert.Contains("[imagem: tela.png]", comment.Content);
    }

    /// <summary>Resposta automática, spam e duplicata não baixam anexo nenhum.</summary>
    [Fact]
    public async Task Mensagem_retida_nao_baixa_anexos()
    {
        var world = await SetUpWithMailboxAsync();

        Fixture.Api.Mailbox.Deliver(Mailbox, await EmailOfAsync(world.RequesterId), "Ausente", "Estou de férias.",
            headers: new Dictionary<string, string> { ["auto-submitted"] = "auto-replied" },
            attachments: [new FetchedAttachment("foto.png", "image/png", Png, IsInline: true, ContentId: "foto")]);
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        Assert.False(await db.Tickets.AnyAsync(t => t.Source == TicketSource.Email));
        Assert.Empty(Fixture.Api.Mailbox.AttachmentsFetched);
        Assert.False(await db.TicketAttachments.AnyAsync());
    }

    // ----- Recuperação dos anexos de mensagens antigas -----

    /// <summary>
    /// A abertura e a resposta lidas antes da versão que guarda o <c>ProviderId</c> ganham os
    /// anexos onde a mensagem foi parar — o print da abertura no chamado, o da resposta no
    /// comentário —, sem repetir a imagem da assinatura. Corrigir a exibição não é evento:
    /// nada no histórico, nenhum aviso, o "atualizado em" fica como estava.
    /// </summary>
    [Fact]
    public async Task Recupera_os_anexos_de_mensagens_antigas_sem_gerar_evento()
    {
        var world = await SetUpWithMailboxAsync();
        var from = await EmailOfAsync(world.RequesterId);
        var logo = new FetchedAttachment("logo.png", "image/png", Png, IsInline: true, ContentId: "logo");

        const string openingBody = "Erro ao abrir.\n[cid:print]\n[cid:logo]";
        var opening = Fixture.Api.Mailbox.Deliver(Mailbox, from, "Sistema fora", openingBody);
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.Source == TicketSource.Email);

        const string replyText = "Segue outro print.\n[cid:tela]\n[cid:logo]";
        var reply = Fixture.Api.Mailbox.Deliver(Mailbox, from, $"RE: [{ticket.Code}] Sistema fora", replyText);
        await SyncAsync();

        var comment = await db.TicketComments.AsNoTracking().SingleAsync(c => c.TicketId == ticket.Id);

        // Como as mensagens ficaram na versão anterior: sem identificador e com as marcas.
        await db.InboundEmails.ExecuteUpdateAsync(set => set.SetProperty(e => e.ProviderId, (string?)null));
        await db.Tickets.Where(t => t.Id == ticket.Id).ExecuteUpdateAsync(set => set.SetProperty(t => t.Description, openingBody));
        await db.TicketComments.Where(c => c.Id == comment.Id).ExecuteUpdateAsync(set => set.SetProperty(c => c.Content, replyText));

        Fixture.Api.Mailbox.SetAttachments(opening.ProviderId,
            [new FetchedAttachment("print.png", "image/png", [0x89, 0x50, 7, 7], IsInline: true, ContentId: "print"), logo]);
        Fixture.Api.Mailbox.SetAttachments(reply.ProviderId,
            [new FetchedAttachment("tela.png", "image/png", [0x89, 0x50, 8, 8, 8], IsInline: true, ContentId: "tela"), logo]);

        var updatedAt = await db.Tickets.Where(t => t.Id == ticket.Id).Select(t => t.UpdatedAt).SingleAsync();
        var history = await db.TicketHistory.CountAsync(h => h.TicketId == ticket.Id);
        var notifications = await db.Notifications.CountAsync();

        Assert.Equal(2, await PendingRecoveryAsync(world.Manager));

        var batch = await RunRecoveryAsync(world.Manager);

        Assert.Equal(new AttachmentRecoveryBatch(2, 2, 3, 0, 0, null, null), batch);

        var attachments = await ListAttachmentsAsync(world.Requester, ticket.Id);
        Assert.Equal(["logo.png", "print.png", "tela.png"], attachments.Select(a => a.FileName).Order().ToArray());
        Assert.Null(attachments.Single(a => a.FileName == "print.png").CommentId);
        Assert.Null(attachments.Single(a => a.FileName == "logo.png").CommentId);
        Assert.Equal(comment.Id, attachments.Single(a => a.FileName == "tela.png").CommentId);
        Assert.All(attachments, a => Assert.Equal(world.RequesterId, a.UploadedById));

        var description = await db.Tickets.Where(t => t.Id == ticket.Id).Select(t => t.Description).SingleAsync();
        Assert.Contains("[imagem: print.png]", description);
        Assert.Contains("[imagem: logo.png]", description);

        var content = await db.TicketComments.Where(c => c.Id == comment.Id).Select(c => c.Content).SingleAsync();
        Assert.Contains("[imagem: tela.png]", content);

        Assert.Equal(updatedAt, await db.Tickets.Where(t => t.Id == ticket.Id).Select(t => t.UpdatedAt).SingleAsync());
        Assert.Equal(history, await db.TicketHistory.CountAsync(h => h.TicketId == ticket.Id));
        Assert.Equal(notifications, await db.Notifications.CountAsync());

        // Repetir não traz nada de novo.
        Assert.Equal(0, await PendingRecoveryAsync(world.Manager));
        Assert.Equal(0, (await RunRecoveryAsync(world.Manager)).Examined);
        Assert.Equal(3, (await ListAttachmentsAsync(world.Requester, ticket.Id)).Count);
    }

    /// <summary>
    /// Mensagem apagada da caixa de correio não tem de onde trazer: conta como não achada e
    /// continua na fila, sem impedir as outras do lote.
    /// </summary>
    [Fact]
    public async Task Mensagem_apagada_da_caixa_fica_de_fora_da_recuperacao()
    {
        var world = await SetUpWithMailboxAsync();
        var from = await EmailOfAsync(world.RequesterId);

        var gone = Fixture.Api.Mailbox.Deliver(Mailbox, from, "Impressora", "Não imprime.\n[cid:foto]");
        var kept = Fixture.Api.Mailbox.Deliver(Mailbox, from, "Monitor", "Piscando.\n[cid:foto2]");
        await SyncAsync();

        await using var db = Fixture.CreateContext();
        await db.InboundEmails.ExecuteUpdateAsync(set => set.SetProperty(e => e.ProviderId, (string?)null));

        Fixture.Api.Mailbox.Remove(Mailbox, gone.MessageId!);
        Fixture.Api.Mailbox.SetAttachments(kept.ProviderId,
            [new FetchedAttachment("foto2.png", "image/png", Png, IsInline: true, ContentId: "foto2")]);

        var batch = await RunRecoveryAsync(world.Manager);

        Assert.Equal(2, batch.Examined);
        Assert.Equal(1, batch.Recovered);
        Assert.Equal(1, batch.NotFound);
        Assert.Equal(1, await PendingRecoveryAsync(world.Manager));
    }

    [Fact]
    public async Task So_administrador_recupera_anexos()
    {
        var world = await SetUpWithMailboxAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await world.Technician.PostAsync("/api/inbound/attachment-recovery", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await world.Requester.GetAsync("/api/inbound/attachment-recovery")).StatusCode);
    }

    private static async Task<int> PendingRecoveryAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<Dictionary<string, int>>("/api/inbound/attachment-recovery", TestJson.Options);

        return body!["pending"];
    }

    private static async Task<AttachmentRecoveryBatch> RunRecoveryAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/inbound/attachment-recovery", null);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<AttachmentRecoveryBatch>())!;
    }

    // ----- Atalhos -----

    private static async Task<List<AttachmentItem>> ListAttachmentsAsync(HttpClient client, Guid ticketId)
    {
        var response = await client.GetAsync($"/api/tickets/{ticketId}/attachments");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<List<AttachmentItem>>())!;
    }

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
