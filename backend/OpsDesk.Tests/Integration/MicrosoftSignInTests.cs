using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Api.Endpoints;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Notifications;
using OpsDesk.Application.Settings;
using OpsDesk.Application.Users;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Login com a conta Microsoft: disponibilidade, o fluxo pelo navegador, o vínculo com conta
/// existente e o que é recusado (docs/arquitetura.md, decisão sobre o login Microsoft).
/// </summary>
[Collection(PostgresCollection.Name)]
public class MicrosoftSignInTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    private const string TenantId = "0f6c5e3a-1b2c-4d5e-8f90-a1b2c3d4e5f6";
    private const string ClientId = "11111111-2222-3333-4444-555555555555";
    private const string PortalUrl = "https://opsdesk.empresa.com";

    // ----- Disponibilidade -----

    [Fact]
    public async Task So_oferece_o_botao_ligado_e_com_o_aplicativo_configurado()
    {
        var world = await SetUpAsync();

        Assert.False((await OptionsAsync()).MicrosoftSignInAvailable);

        // Ligado sem aplicativo do Entra ID: a configuração avisa, o login não mostra.
        await EnableMicrosoftAsync(world.Manager);
        var general = await world.Manager.GetFromJsonAsync<GeneralSettings>("/api/admin/settings/general", TestJson.Options);

        Assert.True(general!.MicrosoftSignInEnabled);
        Assert.False(general.MicrosoftSignInReady);
        Assert.False((await OptionsAsync()).MicrosoftSignInAvailable);

        await ConfigureEntraAppAsync(world.Manager, TenantId);

        Assert.True((await OptionsAsync()).MicrosoftSignInAvailable);
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    public async Task Tenant_generico_nao_habilita_o_login(string tenant)
    {
        // Esses endereços aceitam conta de qualquer empresa: todo mundo viraria solicitante.
        var world = await SetUpAsync();
        await EnableMicrosoftAsync(world.Manager);
        await ConfigureEntraAppAsync(world.Manager, tenant);

        Assert.False((await OptionsAsync()).MicrosoftSignInAvailable);
    }

    [Fact]
    public async Task Inicio_sem_o_login_disponivel_volta_ao_login_com_o_motivo()
    {
        await SetUpAsync();
        using var browser = Fixture.Api.CreateBrowserClient();

        var response = await browser.GetAsync("/api/auth/microsoft/start?returnTo=https://localhost/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://localhost/entrar?microsoft=indisponivel", response.Headers.Location!.ToString());
    }

    // ----- O fluxo -----

    [Fact]
    public async Task Inicio_leva_ao_entra_id_com_pkce_state_e_nonce()
    {
        await SetUpReadyAsync();
        using var browser = Fixture.Api.CreateBrowserClient();

        var response = await browser.GetAsync("/api/auth/microsoft/start?returnTo=https://localhost/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location!;
        var query = QueryHelpers.ParseQuery(location.Query);

        Assert.Equal($"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/authorize", location.GetLeftPart(UriPartial.Path));
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("query", query["response_mode"]);
        Assert.Equal("https://localhost/api/auth/microsoft/callback", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.False(string.IsNullOrEmpty(query["nonce"]));

        // O estado fica no navegador, cifrado e restrito às rotas do fluxo.
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(MicrosoftSignInEndpoints.FlowCookieName));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth/microsoft", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Primeiro_login_cria_solicitante_sem_senha_e_abre_a_sessao()
    {
        await SetUpReadyAsync();
        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-ana", "Ana.Souza@Empresa.com", "Ana Souza");
        using var browser = Fixture.Api.CreateBrowserClient();

        var (callback, challenge, nonce) = await CompleteAsync(browser, "https://localhost/chamados/123");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("https://localhost/chamados/123", callback.Headers.Location!.ToString());

        // O código foi trocado com o verificador que gerou o desafio, e com o nonce do fluxo.
        var redeemed = Assert.Single(Fixture.Api.Entra.Redeemed);
        Assert.Equal("codigo-do-entra", redeemed.Code);
        Assert.Equal("https://localhost/api/auth/microsoft/callback", redeemed.RedirectUri);
        Assert.Equal(challenge, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(redeemed.CodeVerifier))));
        Assert.Equal(nonce, redeemed.Nonce);

        // A sessão é a de sempre: o SPA carrega e renova pelo cookie.
        var session = await RefreshAsync(browser);
        Assert.Equal("ana.souza@empresa.com", session.User.Email);
        Assert.Equal("Ana Souza", session.User.Name);
        Assert.Equal(UserRole.Requester, session.User.Role);

        await using var db = Fixture.CreateContext();
        var user = await db.Users.SingleAsync(u => u.Email == "ana.souza@empresa.com");
        Assert.Null(user.PasswordHash);
        Assert.Equal("oid-ana", user.EntraObjectId);
    }

    [Fact]
    public async Task Conta_existente_e_vinculada_pelo_email_e_mantem_o_perfil()
    {
        var world = await SetUpReadyAsync();
        var email = await EmailOfAsync(world.TechnicianId);

        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-tecnico", email.ToUpperInvariant(), "Outro Nome");
        using var browser = Fixture.Api.CreateBrowserClient();
        await CompleteAsync(browser);

        var session = await RefreshAsync(browser);
        Assert.Equal(world.TechnicianId, session.User.Id);
        Assert.Equal(UserRole.Technician, session.User.Role);

        var history = await world.Manager.GetFromJsonAsync<List<UserAuditItem>>(
            $"/api/admin/users/{world.TechnicianId}/history", TestJson.Options);
        Assert.Contains(history!, h => h.Action == UserAuditAction.MicrosoftAccountLinked && h.NewValue == "oid-tecnico");

        // Depois do vínculo, quem reconhece a pessoa é o oid: o e-mail pode mudar no Entra ID.
        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-tecnico", "novo.endereco@empresa.com", null);
        using var again = Fixture.Api.CreateBrowserClient();
        await CompleteAsync(again);

        Assert.Equal(world.TechnicianId, (await RefreshAsync(again)).User.Id);
    }

    [Fact]
    public async Task Email_de_conta_ligada_a_outra_identidade_nao_assume_a_conta()
    {
        var world = await SetUpReadyAsync();
        var email = await EmailOfAsync(world.RequesterId);

        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-original", email, null);
        using (var first = Fixture.Api.CreateBrowserClient())
        {
            await CompleteAsync(first);
        }

        // Mesma caixa, outra identidade: conta recriada no tenant ou e-mail reaproveitado.
        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-intruso", email, null);
        using var browser = Fixture.Api.CreateBrowserClient();
        var (callback, _, _) = await CompleteAsync(browser);

        Assert.Equal("https://localhost/entrar?microsoft=conflito", callback.Headers.Location!.ToString());
        await AssertNoSessionAsync(browser);
    }

    [Fact]
    public async Task Conta_desativada_nao_entra()
    {
        var world = await SetUpReadyAsync();
        (await world.Manager.PostAsJsonAsync($"/api/admin/users/{world.RequesterId}/activation",
            new ChangeActivationRequest(false), TestJson.Options)).EnsureSuccessStatusCode();

        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-x", await EmailOfAsync(world.RequesterId), null);
        using var browser = Fixture.Api.CreateBrowserClient();
        var (callback, _, _) = await CompleteAsync(browser);

        Assert.Equal("https://localhost/entrar?microsoft=desativada", callback.Headers.Location!.ToString());
        await AssertNoSessionAsync(browser);
    }

    // ----- O que é recusado -----

    [Fact]
    public async Task Retorno_que_nao_e_do_fluxo_deste_navegador_e_recusado()
    {
        // Login CSRF: o atacante inicia o fluxo, pega o código da própria conta e manda o
        // link de retorno para a vítima, que acabaria logada como ele.
        await SetUpReadyAsync();
        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-atacante", "atacante@empresa.com", null);

        using var attacker = Fixture.Api.CreateBrowserClient();
        var start = await attacker.GetAsync("/api/auth/microsoft/start?returnTo=https://localhost/");
        var state = QueryHelpers.ParseQuery(start.Headers.Location!.Query)["state"];

        using var victim = Fixture.Api.CreateBrowserClient();
        var callback = await victim.GetAsync($"/api/auth/microsoft/callback?code=codigo&state={state}");

        // Sem o fluxo, não se sabe de onde a pessoa veio: volta ao portal configurado.
        Assert.Equal($"{PortalUrl}/entrar?microsoft=expirou", callback.Headers.Location!.ToString());
        Assert.Empty(Fixture.Api.Entra.Redeemed);
        await AssertNoSessionAsync(victim);
    }

    [Fact]
    public async Task State_diferente_do_fluxo_e_recusado()
    {
        await SetUpReadyAsync();
        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-ana", "ana@empresa.com", null);
        using var browser = Fixture.Api.CreateBrowserClient();

        await browser.GetAsync("/api/auth/microsoft/start?returnTo=https://localhost/");
        var callback = await browser.GetAsync("/api/auth/microsoft/callback?code=codigo&state=outro");

        Assert.Equal("https://localhost/entrar?microsoft=expirou", callback.Headers.Location!.ToString());
        Assert.Empty(Fixture.Api.Entra.Redeemed);
    }

    [Fact]
    public async Task Desistencia_no_entra_id_volta_ao_login_como_cancelado()
    {
        await SetUpReadyAsync();
        using var browser = Fixture.Api.CreateBrowserClient();

        var start = await browser.GetAsync("/api/auth/microsoft/start?returnTo=https://localhost/");
        var state = QueryHelpers.ParseQuery(start.Headers.Location!.Query)["state"];
        var callback = await browser.GetAsync($"/api/auth/microsoft/callback?error=access_denied&state={state}");

        Assert.Equal("https://localhost/entrar?microsoft=cancelado", callback.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Id_token_recusado_nao_abre_sessao()
    {
        await SetUpReadyAsync();
        Fixture.Api.Entra.NextIdentity = null;
        using var browser = Fixture.Api.CreateBrowserClient();

        var (callback, _, _) = await CompleteAsync(browser);

        Assert.Equal("https://localhost/entrar?microsoft=falhou", callback.Headers.Location!.ToString());
        await AssertNoSessionAsync(browser);
    }

    [Theory]
    [InlineData("https://golpe.example/entrar")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/caminho-relativo")]
    public async Task Destino_de_origem_desconhecida_vira_o_portal(string returnTo)
    {
        // Sem isto, um link "entre com a Microsoft" deixaria a pessoa num site falso depois
        // de um login de verdade.
        await SetUpReadyAsync();
        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-ana", "ana@empresa.com", null);
        using var browser = Fixture.Api.CreateBrowserClient();

        var (callback, _, _) = await CompleteAsync(browser, returnTo);

        Assert.Equal($"{PortalUrl}/", callback.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Destino_na_origem_do_portal_e_aceito()
    {
        // O portal pode ter endereço próprio, diferente do da API que recebe o retorno.
        await SetUpReadyAsync();
        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-ana", "ana@empresa.com", null);
        using var browser = Fixture.Api.CreateBrowserClient();

        var (callback, _, _) = await CompleteAsync(browser, $"{PortalUrl}/chamados");

        Assert.Equal($"{PortalUrl}/chamados", callback.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Fluxo_serve_uma_vez_so()
    {
        await SetUpReadyAsync();
        Fixture.Api.Entra.NextIdentity = new EntraIdentity("oid-ana", "ana@empresa.com", null);
        using var browser = Fixture.Api.CreateBrowserClient();

        var start = await browser.GetAsync("/api/auth/microsoft/start?returnTo=https://localhost/");
        var state = QueryHelpers.ParseQuery(start.Headers.Location!.Query)["state"];

        await browser.GetAsync($"/api/auth/microsoft/callback?code=codigo&state={state}");
        var replay = await browser.GetAsync($"/api/auth/microsoft/callback?code=codigo&state={state}");

        Assert.Equal($"{PortalUrl}/entrar?microsoft=expirou", replay.Headers.Location!.ToString());
        Assert.Single(Fixture.Api.Entra.Redeemed);
    }

    // ----- Atalhos -----

    private new async Task<World> SetUpAsync()
    {
        Fixture.Api.Entra.Reset();

        return await base.SetUpAsync();
    }

    private async Task<World> SetUpReadyAsync()
    {
        var world = await SetUpAsync();

        await EnableMicrosoftAsync(world.Manager);
        await ConfigureEntraAppAsync(world.Manager, TenantId);

        return world;
    }

    /// <summary>Faz o caminho do navegador: início, Entra ID (simulado) e retorno.</summary>
    private static async Task<(HttpResponseMessage Callback, string Challenge, string Nonce)> CompleteAsync(
        HttpClient browser, string returnTo = "https://localhost/")
    {
        var start = await browser.GetAsync($"/api/auth/microsoft/start?returnTo={Uri.EscapeDataString(returnTo)}");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);

        var query = QueryHelpers.ParseQuery(start.Headers.Location!.Query);

        var callback = await browser.GetAsync(
            $"/api/auth/microsoft/callback?code=codigo-do-entra&state={Uri.EscapeDataString(query["state"]!)}");

        return (callback, query["code_challenge"]!, query["nonce"]!);
    }

    private static async Task<AuthEndpoints.AuthResponse> RefreshAsync(HttpClient browser)
    {
        var response = await browser.PostAsync("/api/auth/refresh", null);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>())!;
    }

    private static async Task AssertNoSessionAsync(HttpClient browser) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsync("/api/auth/refresh", null)).StatusCode);

    private async Task<PublicSettings> OptionsAsync()
    {
        using var anonymous = Fixture.Api.CreateBrowserClient();

        return (await anonymous.GetFromJsonAsync<PublicSettings>("/api/auth/options", TestJson.Options))!;
    }

    private static async Task EnableMicrosoftAsync(HttpClient manager) =>
        (await manager.PutAsJsonAsync("/api/admin/settings/general",
            new UpdateGeneralSettingsRequest(null, true, null, MicrosoftSignInEnabled: true))).EnsureSuccessStatusCode();

    /// <summary>
    /// O aplicativo do Entra ID mora na configuração de e-mail. O envio fica desligado: o
    /// login não depende dele.
    /// </summary>
    private static async Task ConfigureEntraAppAsync(HttpClient manager, string tenant) =>
        (await manager.PutAsJsonAsync("/api/admin/settings/email", new UpdateEmailSettingsRequest(
            false, null, null, tenant, ClientId, "segredo", null, PortalUrl))).EnsureSuccessStatusCode();

    private async Task<string> EmailOfAsync(Guid userId)
    {
        await using var db = Fixture.CreateContext();

        return await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
