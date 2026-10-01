using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Api.Endpoints;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Notifications;
using OpsDesk.Application.Settings;
using OpsDesk.Application.Users;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Contas: política de autocadastro, cadastro e redefinição pelo gestor, "esqueci minha
/// senha" e a trilha de auditoria de usuário (README, seções 13.9, 13.12 e 13.13).
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class AccountTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    // ----- Política de autocadastro -----

    [Fact]
    public async Task Opcoes_publicas_respondem_sem_sessao()
    {
        await SetUpAsync();
        using var anonymous = Fixture.Api.CreateBrowserClient();

        var options = await anonymous.GetFromJsonAsync<PublicSettings>("/api/auth/options", TestJson.Options);

        Assert.True(options!.AllowSelfRegistration);
        Assert.False(options.PasswordResetAvailable);
        Assert.Equal("OpsDesk", options.OrganizationName);
    }

    [Fact]
    public async Task Cadastro_fechado_recusa_o_autocadastro()
    {
        var world = await SetUpAsync();
        await UpdateGeneralAsync(world.Manager, new UpdateGeneralSettingsRequest(null, false, null));

        var response = await RegisterAsync("alguem@empresa.com");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("maria@empresa.com.br", true)]
    [InlineData("MARIA@Empresa.com", true)]
    [InlineData("maria@gmail.com", false)]
    [InlineData("maria@golpe-empresa.com", false)]
    [InlineData("maria@empresa.com.evil.io", false)]
    public async Task Autocadastro_so_aceita_os_dominios_liberados(string email, bool accepted)
    {
        var world = await SetUpAsync();
        await UpdateGeneralAsync(world.Manager, new UpdateGeneralSettingsRequest("ACME", true, ["empresa.com", "@empresa.com.br"]));

        var response = await RegisterAsync(email);

        Assert.Equal(accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Configuracao_geral_e_so_do_gestor_e_e_auditada()
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await world.Technician.GetAsync("/api/admin/settings/general")).StatusCode);

        await UpdateGeneralAsync(world.Manager, new UpdateGeneralSettingsRequest("Central de TI ACME", true, null));

        await using var db = Fixture.CreateContext();
        var audit = await db.SettingsAudit.SingleAsync(a => a.Area == SystemSettingsService.AuditArea);
        Assert.Equal(world.ManagerId, audit.ChangedById);
        Assert.Contains("OrganizationName", audit.ChangedFields);
    }

    // ----- Cadastro pelo gestor -----

    [Fact]
    public async Task Sem_email_o_gestor_recebe_senha_provisoria_que_obriga_a_troca()
    {
        var world = await SetUpAsync();

        var access = await CreateUserAsync(world.Manager, "nova.tecnica@empresa.com", UserRole.Technician);

        Assert.False(access.InvitationSent);
        Assert.NotNull(access.TemporaryPassword);

        using var client = Fixture.Api.CreateBrowserClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nova.tecnica@empresa.com", access.TemporaryPassword!));
        var session = await login.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();
        Assert.True(session!.User.MustChangePassword);

        var created = Assert.Single(await UserHistoryAsync(world.Manager, access.User.Id));
        Assert.Equal(UserAuditAction.Created, created.Action);
        Assert.Equal("Technician", created.NewValue);
        Assert.NotNull(created.ChangedByName);
    }

    [Fact]
    public async Task Com_email_a_pessoa_recebe_convite_e_define_a_propria_senha()
    {
        var world = await SetUpAsync();
        await EnableEmailAsync(world.Manager);

        var access = await CreateUserAsync(world.Manager, "convidado@empresa.com", UserRole.Requester);

        Assert.True(access.InvitationSent);
        Assert.Null(access.TemporaryPassword);

        var token = await LastLinkTokenAsync("convidado@empresa.com");
        using var anonymous = Fixture.Api.CreateBrowserClient();

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(anonymous, token, "senha-escolhida-123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("convidado@empresa.com", "senha-escolhida-123"))).StatusCode);

        Assert.Contains(await UserHistoryAsync(world.Manager, access.User.Id), h => h.Action == UserAuditAction.PasswordSetByLink);
    }

    [Fact]
    public async Task Email_com_link_e_apagado_da_fila_depois_do_envio()
    {
        var world = await SetUpAsync();
        await EnableEmailAsync(world.Manager);
        await CreateUserAsync(world.Manager, "convidado@empresa.com", UserRole.Requester);

        await DispatchAsync();

        await using var db = Fixture.CreateContext();
        var email = await db.OutboundEmails.SingleAsync(e => e.ToAddress == "convidado@empresa.com");
        Assert.Equal(OutboundEmailStatus.Sent, email.Status);
        Assert.Equal(EmailOutboxService.RedactedBody, email.HtmlBody);
        Assert.Contains(Fixture.Api.Email.Sent, s => s.Message.HtmlBody.Contains("redefinir-senha?token="));
    }

    [Fact]
    public async Task Email_repetido_e_recusado()
    {
        var world = await SetUpAsync();
        await CreateUserAsync(world.Manager, "repetido@empresa.com", UserRole.Requester);

        var response = await world.Manager.PostAsJsonAsync(
            "/api/admin/users", new CreateUserRequest("Outra", "Repetido@Empresa.com", UserRole.Requester), TestJson.Options);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task So_gestor_cadastra_redefine_e_ve_a_trilha()
    {
        var world = await SetUpAsync();

        foreach (var client in new[] { world.Technician, world.Requester })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
                "/api/admin/users", new CreateUserRequest("X", "x@empresa.com", UserRole.Manager), TestJson.Options)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsync($"/api/admin/users/{world.RequesterId}/password-reset", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync($"/api/admin/users/{world.RequesterId}/history")).StatusCode);
        }
    }

    // ----- Redefinição pelo gestor -----

    [Fact]
    public async Task Redefinir_sem_email_mata_a_senha_antiga_e_as_sessoes()
    {
        var world = await SetUpAsync();

        var response = await world.Manager.PostAsync($"/api/admin/users/{world.RequesterId}/password-reset", null);
        var access = await response.Content.ReadJsonAsync<AccessGranted>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(access!.TemporaryPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, (await world.Requester.PostAsync("/api/auth/refresh", null)).StatusCode);

        var email = await EmailOfAsync(world.RequesterId);
        using var client = Fixture.Api.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, access.TemporaryPassword!))).StatusCode);

        Assert.Contains(await UserHistoryAsync(world.Manager, world.RequesterId),
            h => h.Action == UserAuditAction.PasswordReset && h.ChangedByName != null);
    }

    [Fact]
    public async Task Redefinir_com_email_tambem_mata_a_senha_antiga_ate_usar_o_link()
    {
        var world = await SetUpAsync();
        await EnableEmailAsync(world.Manager);

        var access = await (await world.Manager.PostAsync($"/api/admin/users/{world.RequesterId}/password-reset", null))
            .Content.ReadJsonAsync<AccessGranted>();

        Assert.True(access!.InvitationSent);

        var email = await EmailOfAsync(world.RequesterId);
        using var client = Fixture.Api.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password))).StatusCode);

        await ResetAsync(client, await LastLinkTokenAsync(email), "senha-nova-depois-do-link");
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "senha-nova-depois-do-link"))).StatusCode);
    }

    [Fact]
    public async Task Gestor_nao_redefine_a_propria_senha_por_aqui()
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.Conflict,
            (await world.Manager.PostAsync($"/api/admin/users/{world.ManagerId}/password-reset", null)).StatusCode);
    }

    // ----- Esqueci minha senha -----

    [Fact]
    public async Task Pedido_responde_igual_exista_ou_nao_a_conta()
    {
        var world = await SetUpAsync();
        await EnableEmailAsync(world.Manager);
        using var anonymous = Fixture.Api.CreateBrowserClient();

        var unknown = await ForgotAsync(anonymous, "ninguem@empresa.com");
        var known = await ForgotAsync(anonymous, await EmailOfAsync(world.RequesterId));

        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await known.Content.ReadAsStringAsync());

        await using var db = Fixture.CreateContext();
        Assert.False(await db.OutboundEmails.AnyAsync(e => e.ToAddress == "ninguem@empresa.com"));
        Assert.True(await db.OutboundEmails.AnyAsync(e => e.IsSensitive));
    }

    [Fact]
    public async Task Link_funciona_uma_vez_e_derruba_as_sessoes()
    {
        var world = await SetUpAsync();
        await EnableEmailAsync(world.Manager);
        var email = await EmailOfAsync(world.RequesterId);
        using var anonymous = Fixture.Api.CreateBrowserClient();

        await ForgotAsync(anonymous, email);
        var token = await LastLinkTokenAsync(email);

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(anonymous, token, "primeira-troca-123")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(anonymous, token, "segunda-troca-123")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await world.Requester.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Pedido_novo_invalida_o_link_anterior()
    {
        var world = await SetUpAsync();
        await EnableEmailAsync(world.Manager);
        var email = await EmailOfAsync(world.RequesterId);
        using var anonymous = Fixture.Api.CreateBrowserClient();

        await ForgotAsync(anonymous, email);
        var first = await LastLinkTokenAsync(email);
        await ForgotAsync(anonymous, email);
        var second = await LastLinkTokenAsync(email);

        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(anonymous, first, "senha-qualquer-123")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(anonymous, second, "senha-qualquer-123")).StatusCode);
    }

    [Fact]
    public async Task Link_vencido_ou_de_conta_desativada_nao_vale()
    {
        var world = await SetUpAsync();
        await EnableEmailAsync(world.Manager);
        using var anonymous = Fixture.Api.CreateBrowserClient();

        var requesterEmail = await EmailOfAsync(world.RequesterId);
        await ForgotAsync(anonymous, requesterEmail);
        var expired = await LastLinkTokenAsync(requesterEmail);

        var otherEmail = await EmailOfAsync(world.OtherRequesterId);
        await ForgotAsync(anonymous, otherEmail);
        var deactivated = await LastLinkTokenAsync(otherEmail);

        await using (var db = Fixture.CreateContext())
        {
            await db.PasswordResetTokens.Where(t => t.UserId == world.RequesterId)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await db.Users.Where(u => u.Id == world.OtherRequesterId)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.IsActive, false));
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(anonymous, expired, "senha-qualquer-123")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(anonymous, deactivated, "senha-qualquer-123")).StatusCode);
    }

    [Fact]
    public async Task Sem_email_ligado_o_pedido_nao_gera_nada()
    {
        var world = await SetUpAsync();
        using var anonymous = Fixture.Api.CreateBrowserClient();

        Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(anonymous, await EmailOfAsync(world.RequesterId))).StatusCode);

        await using var db = Fixture.CreateContext();
        Assert.False(await db.PasswordResetTokens.AnyAsync());
        Assert.False(await db.OutboundEmails.AnyAsync());
    }

    // ----- Trilha de auditoria -----

    [Fact]
    public async Task Trilha_registra_perfil_ativacao_e_troca_de_senha_com_o_autor()
    {
        var world = await SetUpAsync();

        await world.Manager.PostAsJsonAsync($"/api/admin/users/{world.RequesterId}/role", new ChangeRoleRequest(UserRole.Technician), TestJson.Options);
        await world.Manager.PostAsJsonAsync($"/api/admin/users/{world.RequesterId}/activation", new ChangeActivationRequest(false));
        await world.Manager.PostAsJsonAsync($"/api/admin/users/{world.RequesterId}/activation", new ChangeActivationRequest(true));
        await world.OtherRequester.PostAsJsonAsync("/api/auth/password",
            new ChangePasswordRequest(Password, "senha-nova-trocada", "senha-nova-trocada"));

        var history = await UserHistoryAsync(world.Manager, world.RequesterId);
        var role = Assert.Single(history, h => h.Action == UserAuditAction.RoleChanged);
        Assert.Equal("Requester", role.PreviousValue);
        Assert.Equal("Technician", role.NewValue);
        Assert.Contains(history, h => h.Action == UserAuditAction.Deactivated);
        Assert.Contains(history, h => h.Action == UserAuditAction.Reactivated);

        var own = await UserHistoryAsync(world.Manager, world.OtherRequesterId);
        Assert.Contains(own, h => h.Action == UserAuditAction.PasswordChanged);
    }

    [Theory]
    [InlineData("/api/admin/settings/general")]
    [InlineData("/api/admin/holidays")]
    [InlineData("/api/inbound")]
    [InlineData("/api/admin/settings/email/accounts")]
    public async Task Rotas_novas_recusam_senha_provisoria(string route)
    {
        var world = await SetUpAsync();

        var access = await CreateUserAsync(world.Manager, "gestor.novo@empresa.com", UserRole.Manager);
        using var client = Fixture.Api.CreateBrowserClient();
        var session = await (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("gestor.novo@empresa.com", access.TemporaryPassword!))).Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("password-change-required", await response.Content.ReadAsStringAsync());
    }

    // ----- Fila com várias réplicas -----

    [Fact]
    public async Task Duas_replicas_despachando_juntas_enviam_cada_email_uma_vez()
    {
        var world = await SetUpAsync();
        await EnableEmailAsync(world.Manager);

        for (var i = 0; i < 5; i++)
        {
            await ForgotAsync(Fixture.Api.CreateBrowserClient(), await EmailOfAsync(world.RequesterId));
        }

        var sent = await Task.WhenAll(DispatchAsync(), DispatchAsync());

        Assert.Equal(5, sent.Sum());
        Assert.Equal(5, Fixture.Api.Email.Sent.Count);
    }

    // ----- Atalhos -----

    private new async Task<World> SetUpAsync()
    {
        Fixture.Api.Email.Reset();

        return await base.SetUpAsync();
    }

    private async Task<HttpResponseMessage> RegisterAsync(string email)
    {
        using var client = Fixture.Api.CreateBrowserClient();

        return await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Maria", email, Password, Password));
    }

    private static async Task UpdateGeneralAsync(HttpClient manager, UpdateGeneralSettingsRequest request) =>
        (await manager.PutAsJsonAsync("/api/admin/settings/general", request)).EnsureSuccessStatusCode();

    private static async Task EnableEmailAsync(HttpClient manager) =>
        (await manager.PutAsJsonAsync("/api/admin/settings/email", new UpdateEmailSettingsRequest(
            true, "suporte@empresa.com", "Suporte", "empresa.onmicrosoft.com",
            "11111111-2222-3333-4444-555555555555", "segredo", null, "https://opsdesk.empresa.com"))).EnsureSuccessStatusCode();

    private static async Task<AccessGranted> CreateUserAsync(HttpClient manager, string email, UserRole role)
    {
        var response = await manager.PostAsJsonAsync("/api/admin/users", new CreateUserRequest("Pessoa Nova", email, role), TestJson.Options);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadJsonAsync<AccessGranted>())!;
    }

    private static async Task<List<UserAuditItem>> UserHistoryAsync(HttpClient manager, Guid userId)
    {
        var response = await manager.GetAsync($"/api/admin/users/{userId}/history");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<List<UserAuditItem>>())!;
    }

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/password/forgot", new ForgotPasswordRequest(email));

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string token, string password) =>
        client.PostAsJsonAsync("/api/auth/password/reset", new ResetPasswordRequest(token, password, password));

    /// <summary>O token do link do último e-mail de definir senha enviado para o endereço.</summary>
    private async Task<string> LastLinkTokenAsync(string email)
    {
        await using var db = Fixture.CreateContext();

        var body = await db.OutboundEmails
            .Where(e => e.ToAddress == email && e.IsSensitive)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.HtmlBody)
            .FirstAsync();

        return Uri.UnescapeDataString(TokenPattern().Match(WebUtility.HtmlDecode(body)).Groups[1].Value);
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

    [GeneratedRegex("token=([^\"&]+)")]
    private static partial Regex TokenPattern();
}
