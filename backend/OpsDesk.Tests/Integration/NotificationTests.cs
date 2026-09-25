using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Application.Common;
using OpsDesk.Application.Notifications;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Notificações de ponta a ponta: da ação na API até a linha na fila e o item no sino.
///
/// As regras finas de destinatário estão em <c>NotificationPlannerTests</c>. Aqui o que
/// se prova é que qualquer caminho que grava gera o aviso, na mesma transação, e que os
/// endpoints novos respeitam dono e visibilidade.
/// </summary>
[Collection(PostgresCollection.Name)]
public class NotificationTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    private const string Secret = "segredo-do-aplicativo-de-teste";

    // ----- Da ação ao aviso -----

    [Fact]
    public async Task Resposta_publica_da_equipe_enfileira_email_ao_solicitante()
    {
        var world = await SetUpWithEmailAsync();
        var ticket = await OpenTicketAsync(world);

        await CommentAsync(world.Technician, ticket, "Reiniciei o concentrador, pode testar?");

        var email = Assert.Single(await OutboxAsync());
        Assert.Equal(await EmailOfAsync(world.RequesterId), email.ToAddress);
        Assert.Contains("Reiniciei o concentrador", email.HtmlBody);
        Assert.Equal(ticket, email.TicketId);
    }

    [Fact]
    public async Task Nota_interna_nao_enfileira_email()
    {
        var world = await SetUpWithEmailAsync();
        var ticket = await OpenTicketAsync(world);

        await CommentAsync(world.Technician, ticket, "Suspeito do certificado.", isInternal: true);

        Assert.Empty(await OutboxAsync());
    }

    [Fact]
    public async Task Enviar_e_fechar_enfileira_um_email_so()
    {
        var world = await SetUpWithEmailAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest("Concluído.", CloseTicket: true));
        response.EnsureSuccessStatusCode();

        Assert.Single(await OutboxAsync());
    }

    [Fact]
    public async Task Envio_desligado_nao_acumula_email_na_fila()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await CommentAsync(world.Technician, ticket, "Resposta.");

        Assert.Empty(await OutboxAsync());
    }

    [Fact]
    public async Task Resposta_do_solicitante_chega_ao_sino_do_responsavel_e_de_mais_ninguem()
    {
        var world = await SetUpWithEmailAsync();
        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);

        await CommentAsync(world.Requester, ticket, "Ainda não funciona.");

        var mine = await NotificationsAsync(world.Technician);
        var item = Assert.Single(mine.Items, n => n.Kind == NotificationKind.RequesterReplied);
        Assert.Equal(ticket, item.TicketId);
        Assert.Equal("OPS-000001", item.TicketCode);

        // O gestor atribuiu, mas o chamado não está no nome dele: nada no sino dele.
        Assert.Empty((await NotificationsAsync(world.Manager)).Items);
        Assert.Empty((await NotificationsAsync(world.OtherTechnician)).Items);

        // E o solicitante não é avisado da própria resposta, nem por e-mail.
        Assert.Empty(await OutboxAsync());
    }

    [Fact]
    public async Task Resposta_da_equipe_chega_ao_sino_do_solicitante_mesmo_com_email_desligado()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await CommentAsync(world.Technician, ticket, "Pode testar agora?");
        await CommentAsync(world.Technician, ticket, "Nota só da equipe.", isInternal: true);

        var item = Assert.Single((await NotificationsAsync(world.Requester)).Items);
        Assert.Equal(NotificationKind.CommentAdded, item.Kind);
        Assert.Empty(await OutboxAsync());
    }

    [Fact]
    public async Task Atribuicao_avisa_quem_recebe_e_quem_perde_mas_nao_quem_fez()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await AssignAsync(world.Manager, ticket, world.TechnicianId);
        await AssignAsync(world.Manager, ticket, world.OtherTechnicianId);

        Assert.Contains((await NotificationsAsync(world.Technician)).Items, n => n.Kind == NotificationKind.Unassigned);
        Assert.Contains((await NotificationsAsync(world.OtherTechnician)).Items, n => n.Kind == NotificationKind.Assigned);
        Assert.Empty((await NotificationsAsync(world.Manager)).Items);
    }

    [Fact]
    public async Task Tecnico_que_assume_o_chamado_nao_e_avisado_disso()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        await AssignAsync(world.Technician, ticket, world.TechnicianId);

        Assert.Empty((await NotificationsAsync(world.Technician)).Items);
    }

    // ----- Endpoints do sino -----

    [Fact]
    public async Task Contagem_leitura_e_marcar_todas()
    {
        var world = await SetUpAsync();
        var first = await OpenTicketAsync(world);
        var second = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, first, world.TechnicianId);
        await AssignAsync(world.Manager, second, world.TechnicianId);

        Assert.Equal(2, await UnreadAsync(world.Technician));

        var one = (await NotificationsAsync(world.Technician)).Items[0];
        Assert.Equal(HttpStatusCode.NoContent,
            (await world.Technician.PostAsync($"/api/notifications/{one.Id}/read", null)).StatusCode);
        Assert.Equal(1, await UnreadAsync(world.Technician));
        Assert.Single((await NotificationsAsync(world.Technician, "?unreadOnly=true")).Items);

        Assert.Equal(HttpStatusCode.NoContent,
            (await world.Technician.PostAsync("/api/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, await UnreadAsync(world.Technician));
    }

    [Fact]
    public async Task Ninguem_le_nem_marca_notificacao_de_outra_pessoa()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);

        var theirs = Assert.Single((await NotificationsAsync(world.Technician)).Items);

        Assert.Empty((await NotificationsAsync(world.OtherTechnician)).Items);
        Assert.Equal(HttpStatusCode.NotFound,
            (await world.OtherTechnician.PostAsync($"/api/notifications/{theirs.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await world.Manager.PostAsync($"/api/notifications/{theirs.Id}/read", null)).StatusCode);

        Assert.Equal(1, await UnreadAsync(world.Technician));
    }

    [Fact]
    public async Task Quem_deixou_de_ver_o_chamado_deixa_de_ver_a_notificacao()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);

        string email;
        await using (var db = Fixture.CreateContext())
        {
            // Rebaixado direto no banco, com o chamado ainda no nome: o caso que a tela de
            // administração impede, e que a notificação precisa aguentar mesmo assim.
            var user = await db.Users.SingleAsync(u => u.Id == world.TechnicianId);
            user.Role = UserRole.Requester;
            email = user.Email;
            await db.SaveChangesAsync();
        }

        using var client = Fixture.Api.CreateBrowserClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new Application.Auth.LoginRequest(email, Password));
        var session = await login.Content.ReadJsonAsync<Api.Endpoints.AuthEndpoints.AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);

        Assert.Empty((await NotificationsAsync(client)).Items);
        Assert.Equal(0, await UnreadAsync(client));
    }

    // ----- Configuração de e-mail -----

    [Fact]
    public async Task Configuracao_nunca_devolve_o_secret_e_o_guarda_cifrado()
    {
        var world = await SetUpWithEmailAsync();

        var response = await world.Manager.GetAsync("/api/admin/settings/email");
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(Secret, json);

        var view = await response.Content.ReadJsonAsync<EmailSettingsView>();
        Assert.True(view!.HasClientSecret);
        Assert.True(view.ClientSecretReadable);

        await using var db = Fixture.CreateContext();
        var stored = (await db.EmailSettings.SingleAsync()).ProtectedClientSecret;
        Assert.NotNull(stored);
        Assert.DoesNotContain(Secret, stored);
    }

    [Fact]
    public async Task Salvar_sem_secret_mantem_o_atual()
    {
        var world = await SetUpWithEmailAsync();

        await using var before = Fixture.CreateContext();
        var original = (await before.EmailSettings.SingleAsync()).ProtectedClientSecret;

        var response = await world.Manager.PutAsJsonAsync("/api/admin/settings/email",
            Settings(clientSecret: null) with { SenderName = "Central de TI" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var after = Fixture.CreateContext();
        var saved = await after.EmailSettings.SingleAsync();
        Assert.Equal(original, saved.ProtectedClientSecret);
        Assert.Equal("Central de TI", saved.SenderName);
    }

    [Fact]
    public async Task Ligar_o_envio_sem_credencial_e_recusado()
    {
        var world = await SetUpAsync();

        var response = await world.Manager.PutAsJsonAsync("/api/admin/settings/email",
            Settings(clientSecret: null) with { TenantId = null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("tenant", body);
        Assert.Contains("client secret", body);
    }

    [Fact]
    public async Task Mudanca_de_configuracao_e_auditada_sem_o_valor_do_secret()
    {
        var world = await SetUpWithEmailAsync();

        await using var db = Fixture.CreateContext();
        var entry = await db.SettingsAudit.SingleAsync();

        Assert.Equal(world.ManagerId, entry.ChangedById);
        Assert.Contains("ClientSecret", entry.ChangedFields);
        Assert.Contains("TenantId", entry.ChangedFields);
        Assert.DoesNotContain(Secret, entry.ChangedFields);
    }

    [Fact]
    public async Task So_gestor_ve_e_altera_a_configuracao()
    {
        var world = await SetUpAsync();

        foreach (var client in new[] { world.Technician, world.Requester })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/settings/email")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PutAsJsonAsync("/api/admin/settings/email", Settings())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync("/api/admin/settings/email/test", new SendTestEmailRequest("x@empresa.com"))).StatusCode);
        }
    }

    [Fact]
    public async Task Email_de_teste_envia_na_hora_e_registra_o_resultado()
    {
        var world = await SetUpWithEmailAsync();

        var ok = await world.Manager.PostAsJsonAsync("/api/admin/settings/email/test", new SendTestEmailRequest("gestor@empresa.com"));

        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        var (credentials, message) = Assert.Single(Fixture.Api.Email.Sent);
        Assert.Equal(Secret, credentials.ClientSecret);
        Assert.Equal("gestor@empresa.com", message.ToAddress);

        Fixture.Api.Email.FailWith = "O Entra ID recusou as credenciais (401): secret expirado.";

        var failed = await world.Manager.PostAsJsonAsync("/api/admin/settings/email/test", new SendTestEmailRequest("gestor@empresa.com"));

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Contains("secret expirado", await failed.Content.ReadAsStringAsync());

        var view = await (await world.Manager.GetAsync("/api/admin/settings/email")).Content.ReadJsonAsync<EmailSettingsView>();
        Assert.Contains("secret expirado", view!.LastError);
        Assert.NotNull(view.LastSucceededAt);
    }

    [Fact]
    public async Task Secret_ilegivel_aparece_na_tela_e_bloqueia_o_envio()
    {
        var world = await SetUpWithEmailAsync();

        await using (var db = Fixture.CreateContext())
        {
            // O que acontece quando as chaves do Data Protection se perdem num deploy.
            await db.EmailSettings.ExecuteUpdateAsync(set => set.SetProperty(s => s.ProtectedClientSecret, "CfDJ8-chave-que-nao-existe"));
        }

        var view = await (await world.Manager.GetAsync("/api/admin/settings/email")).Content.ReadJsonAsync<EmailSettingsView>();
        Assert.True(view!.HasClientSecret);
        Assert.False(view.ClientSecretReadable);

        var test = await world.Manager.PostAsJsonAsync("/api/admin/settings/email/test", new SendTestEmailRequest("gestor@empresa.com"));
        Assert.Equal(HttpStatusCode.Conflict, test.StatusCode);
        Assert.Contains("Informe o secret de novo", await test.Content.ReadAsStringAsync());
    }

    // ----- Fila -----

    [Fact]
    public async Task Despacho_envia_a_fila_e_marca_como_enviado()
    {
        var world = await SetUpWithEmailAsync();
        var ticket = await OpenTicketAsync(world);
        await CommentAsync(world.Technician, ticket, "Resposta.");

        var sent = await DispatchAsync();

        Assert.Equal(1, sent);
        Assert.Single(Fixture.Api.Email.Sent);

        var email = Assert.Single(await OutboxAsync());
        Assert.Equal(OutboundEmailStatus.Sent, email.Status);
        Assert.NotNull(email.SentAt);

        // Enviado não sai de novo.
        Assert.Equal(0, await DispatchAsync());
    }

    [Fact]
    public async Task Falha_de_envio_reagenda_e_desiste_depois_da_ultima_tentativa()
    {
        var world = await SetUpWithEmailAsync();
        var ticket = await OpenTicketAsync(world);
        await CommentAsync(world.Technician, ticket, "Resposta.");

        Fixture.Api.Email.FailWith = "Graph fora do ar.";

        await DispatchAsync();

        var retry = Assert.Single(await OutboxAsync());
        Assert.Equal(OutboundEmailStatus.Pending, retry.Status);
        Assert.Equal(1, retry.Attempts);
        Assert.True(retry.NextAttemptAt > DateTimeOffset.UtcNow);

        // Antes da hora, não tenta de novo.
        await DispatchAsync();
        Assert.Equal(1, Assert.Single(await OutboxAsync()).Attempts);

        for (var attempt = 2; attempt <= EmailOutboxService.MaxAttempts; attempt++)
        {
            await using (var db = Fixture.CreateContext())
            {
                await db.OutboundEmails.ExecuteUpdateAsync(set => set.SetProperty(e => e.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            }

            await DispatchAsync();
        }

        var failed = Assert.Single(await OutboxAsync());
        Assert.Equal(OutboundEmailStatus.Failed, failed.Status);
        Assert.Equal(EmailOutboxService.MaxAttempts, failed.Attempts);
        Assert.Equal("Graph fora do ar.", failed.LastError);
    }

    [Theory]
    [InlineData("/api/notifications")]
    [InlineData("/api/notifications/unread-count")]
    [InlineData("/api/admin/settings/email")]
    public async Task Rotas_novas_recusam_quem_tem_senha_provisoria(string route)
    {
        await SetUpAsync();

        var email = $"provisorio.{Guid.NewGuid():N}@empresa.com";

        await using (var db = Fixture.CreateContext())
        {
            var user = new User { Name = "Gestor provisório", Email = email, Role = UserRole.Manager, MustChangePassword = true };
            user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(user, Password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        using var client = Fixture.Api.CreateBrowserClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new Application.Auth.LoginRequest(email, Password));
        var session = await login.Content.ReadJsonAsync<Api.Endpoints.AuthEndpoints.AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("password-change-required", await response.Content.ReadAsStringAsync());
    }

    // ----- Atalhos -----

    private async Task<World> SetUpWithEmailAsync()
    {
        var world = await SetUpAsync();

        var response = await world.Manager.PutAsJsonAsync("/api/admin/settings/email", Settings());
        response.EnsureSuccessStatusCode();

        return world;
    }

    private new async Task<World> SetUpAsync()
    {
        Fixture.Api.Email.Reset();

        return await base.SetUpAsync();
    }

    private static UpdateEmailSettingsRequest Settings(string? clientSecret = Secret) => new(
        IsEnabled: true,
        SenderAddress: "suporte@empresa.com",
        SenderName: "Suporte TI",
        TenantId: "empresa.onmicrosoft.com",
        ClientId: "11111111-2222-3333-4444-555555555555",
        ClientSecret: clientSecret,
        ClientSecretExpiresOn: new DateOnly(2027, 9, 1),
        PortalUrl: "https://opsdesk.empresa.com");

    private static async Task CommentAsync(HttpClient client, Guid ticket, string content, bool isInternal = false)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments", new AddCommentRequest(content, isInternal));

        response.EnsureSuccessStatusCode();
    }

    private async Task<List<OutboundEmail>> OutboxAsync()
    {
        await using var db = Fixture.CreateContext();

        return await db.OutboundEmails.AsNoTracking().OrderBy(e => e.CreatedAt).ToListAsync();
    }

    private async Task<string> EmailOfAsync(Guid userId)
    {
        await using var db = Fixture.CreateContext();

        return await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync();
    }

    private async Task<int> DispatchAsync()
    {
        using var scope = Fixture.Api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<EmailOutboxService>().DispatchAsync(batchSize: 50);
    }

    private static async Task<PagedResult<NotificationItem>> NotificationsAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/notifications{query}");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<PagedResult<NotificationItem>>())!;
    }

    private static async Task<int> UnreadAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/notifications/unread-count")).Content.ReadJsonAsync<UnreadCount>())!.Count;
}
