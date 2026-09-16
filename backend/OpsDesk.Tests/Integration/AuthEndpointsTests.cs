using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Api.Endpoints;
using OpsDesk.Application.Auth;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Fluxo de autenticação exercitado por HTTP, do cadastro à renovação de sessão.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AuthEndpointsTests(PostgresFixture fixture)
{
    private const string Password = "senha-de-teste-123";

    private static RegisterRequest NewRegistration(string? email = null) => new(
        "Maria de Souza",
        email ?? $"maria.{Guid.NewGuid():N}@empresa.com",
        Password,
        Password);

    // ----- Cadastro -----

    [Fact]
    public async Task Cadastro_cria_solicitante_e_abre_sessao()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var registration = NewRegistration();
        var response = await client.PostAsJsonAsync("/api/auth/register", registration);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();

        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.Equal(registration.Email, body.User.Email);

        // README, seção 13.2: cadastro pelo portal sempre nasce solicitante. Se isto
        // mudar por acidente, alguém ganha painel técnico se cadastrando.
        Assert.Equal(UserRole.Requester, body.User.Role);
    }

    [Fact]
    public async Task Cadastro_normaliza_o_email()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var registration = NewRegistration("  Joao.Silva@Empresa.COM  ");
        var response = await client.PostAsJsonAsync("/api/auth/register", registration);
        var body = await response.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();

        Assert.Equal("joao.silva@empresa.com", body!.User.Email);
    }

    [Fact]
    public async Task Cadastro_com_email_repetido_e_recusado()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var registration = NewRegistration();
        await client.PostAsJsonAsync("/api/auth/register", registration);

        var second = await client.PostAsJsonAsync("/api/auth/register", registration);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Cadastro_com_email_repetido_em_caixa_diferente_tambem_e_recusado()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        await client.PostAsJsonAsync("/api/auth/register", NewRegistration("ana@empresa.com"));

        var second = await client.PostAsJsonAsync(
            "/api/auth/register", NewRegistration("ANA@empresa.com"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("", "a@b.com", "senha-valida-123", "senha-valida-123")]       // sem nome
    [InlineData("Maria", "sem-arroba", "senha-valida-123", "senha-valida-123")] // e-mail inválido
    [InlineData("Maria", "a@b.com", "curta", "curta")]                          // senha curta
    [InlineData("Maria", "a@b.com", "senha-valida-123", "outra-senha-123")]     // confirmação
    public async Task Cadastro_invalido_devolve_erro_de_validacao(
        string name, string email, string password, string confirmation)
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(name, email, password, confirmation));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadJsonAsync<ValidationProblem>();

        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task Senha_do_cadastro_nao_fica_legivel_no_banco()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var registration = NewRegistration();
        await client.PostAsJsonAsync("/api/auth/register", registration);

        await using var db = fixture.CreateContext();
        var hash = await db.Users
            .Where(u => u.Email == registration.Email)
            .Select(u => u.PasswordHash)
            .SingleAsync();

        Assert.NotNull(hash);
        Assert.DoesNotContain(Password, hash);
    }

    [Fact]
    public async Task O_perfil_vem_no_JSON_como_texto_e_nao_como_inteiro()
    {
        // Contrato com o frontend: os tipos do TypeScript são gerados do schema OpenAPI e
        // dependem de o perfil ser uma união fechada de strings. Serializado como inteiro,
        // `role` viraria `number` e os rótulos em português da interface sairiam vazios —
        // sem erro de compilação em lugar nenhum para avisar.
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"role\":\"Requester\"", json);
    }

    // ----- Login -----

    [Fact]
    public async Task Login_com_credencial_correta_abre_sessao()
    {
        await fixture.ResetAsync();
        var registration = await RegisterAsync();

        using var client = fixture.Api.CreateBrowserClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(registration.Email, Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();

        Assert.Equal(registration.Email, body!.User.Email);
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_com_senha_errada_e_com_email_inexistente_dao_a_mesma_resposta()
    {
        // Respostas distintas transformariam o login em consulta de "esta pessoa tem conta
        // aqui". A mensagem e o status têm que ser indistinguíveis.
        await fixture.ResetAsync();
        var registration = await RegisterAsync();

        using var client = fixture.Api.CreateBrowserClient();

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(registration.Email, "senha-errada-123"));

        var unknownEmail = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("ninguem@empresa.com", Password));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        Assert.Equal(
            await Detail(wrongPassword),
            await Detail(unknownEmail));
    }

    [Fact]
    public async Task Login_de_conta_desativada_e_recusado()
    {
        await fixture.ResetAsync();
        var registration = await RegisterAsync();

        await using var db = fixture.CreateContext();
        await db.Users
            .Where(u => u.Email == registration.Email)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));

        using var client = fixture.Api.CreateBrowserClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(registration.Email, Password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_de_conta_sem_senha_definida_e_recusado()
    {
        // Cenário da versão 2.0: solicitante criado a partir de um e-mail recebido existe
        // e é dono de chamados, mas não consegue autenticar até definir senha.
        await fixture.ResetAsync();
        var registration = await RegisterAsync();

        await using var db = fixture.CreateContext();
        await db.Users
            .Where(u => u.Email == registration.Email)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.PasswordHash, (string?)null));

        using var client = fixture.Api.CreateBrowserClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(registration.Email, Password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Toda_recusa_de_login_e_indistinguivel_das_outras()
    {
        // O teste que sustenta a regra: e-mail inexistente, senha errada, conta desativada
        // e conta sem senha têm que devolver exatamente o mesmo status e a mesma mensagem.
        // Qualquer diferença aqui transforma o login em consulta de "esta pessoa trabalha
        // aqui", que é informação que não deveria sair de um sistema interno.
        await fixture.ResetAsync();

        var active = await RegisterAsync();
        var deactivated = await RegisterAsync();
        var withoutPassword = await RegisterAsync();

        await using var db = fixture.CreateContext();

        await db.Users
            .Where(u => u.Email == deactivated.Email)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));

        await db.Users
            .Where(u => u.Email == withoutPassword.Email)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.PasswordHash, (string?)null));

        using var client = fixture.Api.CreateBrowserClient();

        LoginRequest[] rejections =
        [
            new("ninguem@empresa.com", Password),          // e-mail inexistente
            new(active.Email, "senha-errada-123"),         // senha errada
            new(deactivated.Email, Password),              // conta desativada
            new(withoutPassword.Email, Password)           // conta sem senha
        ];

        var responses = new List<(HttpStatusCode Status, string? Detail)>();

        foreach (var rejection in rejections)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", rejection);

            responses.Add((response.StatusCode, await Detail(response)));
        }

        Assert.Single(responses.Select(r => r.Status).Distinct());
        Assert.Single(responses.Select(r => r.Detail).Distinct());
        Assert.Equal(HttpStatusCode.Unauthorized, responses[0].Status);
    }

    // ----- Cookie de refresh -----

    [Fact]
    public async Task O_refresh_token_nunca_aparece_no_corpo_da_resposta()
    {
        // Se aparecesse, seria legível por JavaScript e o cookie httpOnly perderia o
        // sentido. Este teste é o que impede alguém de "facilitar" o frontend depois.
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("refresh", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task O_cookie_de_refresh_e_httpOnly_secure_e_restrito_ao_caminho_de_auth()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());

        var setCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("opsdesk_refresh="));

        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_banco_fica_o_hash_do_refresh_token_e_nao_o_valor()
    {
        await fixture.ResetAsync();

        var cookies = new CookieHandler();
        using var client = fixture.Api.CreateDefaultClient(cookies);
        client.BaseAddress = new Uri("https://localhost");

        await client.PostAsJsonAsync("/api/auth/register", NewRegistration());

        var plainToken = cookies["opsdesk_refresh"];
        Assert.False(string.IsNullOrWhiteSpace(plainToken));

        await using var db = fixture.CreateContext();
        var stored = await db.RefreshTokens.Select(t => t.TokenHash).SingleAsync();

        Assert.NotEqual(plainToken, stored);
    }

    // ----- Renovação -----

    [Fact]
    public async Task Renovacao_devolve_token_novo_e_rotaciona_o_cookie()
    {
        await fixture.ResetAsync();

        var cookies = new CookieHandler();
        using var client = fixture.Api.CreateDefaultClient(cookies);
        client.BaseAddress = new Uri("https://localhost");

        await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var firstCookie = cookies["opsdesk_refresh"];

        var response = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));

        // Um refresh token vale um uso: o cookie tem que ter mudado.
        Assert.NotEqual(firstCookie, cookies["opsdesk_refresh"]);
    }

    [Fact]
    public async Task Renovacao_sem_cookie_e_recusada()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var response = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Renovacoes_concorrentes_nao_derrubam_a_sessao()
    {
        // O bug que este teste fixa: o StrictMode do React executa o efeito de restauração
        // duas vezes, e duas abas abertas fazem o mesmo em produção. Duas renovações
        // paralelas com o mesmo cookie levavam a segunda a apresentar um token já
        // rotacionado, o servidor lia como vazamento e derrubava todas as sessões — logout
        // aparentemente aleatório, provocado pelo próprio cliente legítimo.
        await fixture.ResetAsync();

        var cookies = new CookieHandler();
        using var client = fixture.Api.CreateDefaultClient(cookies);
        client.BaseAddress = new Uri("https://localhost");

        await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var shared = cookies["opsdesk_refresh"];

        // Duas renovações com o mesmo token, como duas abas recarregando juntas.
        var first = await Refresh(shared);
        var second = await Refresh(shared);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // E a sessão continua utilizável depois disso.
        var body = await second.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();
        var latest = SentCookie(second);

        var third = await Refresh(latest);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
    }

    [Fact]
    public async Task Fora_da_janela_de_tolerancia_o_reuso_volta_a_derrubar_tudo()
    {
        // A tolerância é curta de propósito: ela cobre concorrência do próprio cliente,
        // não um token guardado para usar depois.
        await fixture.ResetAsync();

        await using var api = new OpsDeskApiFactory(
            fixture.ConnectionString, refreshGraceSeconds: 0);

        var cookies = new CookieHandler();
        using var client = api.CreateDefaultClient(cookies);
        client.BaseAddress = new Uri("https://localhost");

        await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var stolen = cookies["opsdesk_refresh"];

        await client.PostAsync("/api/auth/refresh", null);

        using var attacker = api.CreateDefaultClient();
        attacker.BaseAddress = new Uri("https://localhost");
        attacker.DefaultRequestHeaders.Add("Cookie", $"opsdesk_refresh={stolen}");

        var replay = await attacker.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        await using var db = fixture.CreateContext();
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.RevokedAt == null));
    }

    [Fact]
    public async Task Reusar_um_refresh_token_ja_rotacionado_derruba_todas_as_sessoes()
    {
        // Apresentar um token já usado só acontece se o cookie vazou ou se alguém está
        // reproduzindo tráfego. A resposta é cortar todas as sessões do usuário: é o
        // único jeito de expulsar quem roubou o token.
        await fixture.ResetAsync();

        // Janela de tolerância desligada: aqui o cenário é vazamento, não concorrência.
        await using var api = new OpsDeskApiFactory(
            fixture.ConnectionString, refreshGraceSeconds: 0);

        var cookies = new CookieHandler();
        using var client = api.CreateDefaultClient(cookies);
        client.BaseAddress = new Uri("https://localhost");

        await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var stolen = cookies["opsdesk_refresh"];

        // Cliente legítimo renova, e o token antigo fica para trás.
        await client.PostAsync("/api/auth/refresh", null);
        var legitimate = cookies["opsdesk_refresh"];

        // O atacante tenta usar o token antigo.
        using var attacker = api.CreateDefaultClient();
        attacker.BaseAddress = new Uri("https://localhost");
        attacker.DefaultRequestHeaders.Add("Cookie", $"opsdesk_refresh={stolen}");

        var replay = await attacker.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // E o token do cliente legítimo também deixou de valer.
        using var victim = api.CreateDefaultClient();
        victim.BaseAddress = new Uri("https://localhost");
        victim.DefaultRequestHeaders.Add("Cookie", $"opsdesk_refresh={legitimate}");

        var afterBreach = await victim.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, afterBreach.StatusCode);

        await using var db = fixture.CreateContext();
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.RevokedAt == null));
    }

    [Fact]
    public async Task A_cadeia_de_rotacao_fica_auditavel()
    {
        await fixture.ResetAsync();

        using var client = fixture.Api.CreateBrowserClient();
        await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        await client.PostAsync("/api/auth/refresh", null);

        await using var db = fixture.CreateContext();
        var tokens = await db.RefreshTokens.OrderBy(t => t.CreatedAt).ToListAsync();

        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens[0].RevokedAt);
        Assert.Equal(tokens[1].TokenHash, tokens[0].ReplacedByTokenHash);
        Assert.Null(tokens[1].RevokedAt);
    }

    // ----- Logout -----

    [Fact]
    public async Task Logout_revoga_o_token_e_apaga_o_cookie()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        await client.PostAsJsonAsync("/api/auth/register", NewRegistration());

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);

        await using var db = fixture.CreateContext();
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.RevokedAt == null));
    }

    [Fact]
    public async Task Logout_sem_sessao_nao_e_erro()
    {
        // O cliente pode estar com cookie antigo. Falhar aqui o deixaria preso a uma
        // sessão que não existe mais.
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ----- Sessão atual -----

    [Fact]
    public async Task Me_sem_token_e_recusado()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_devolve_o_usuario_autenticado()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var registration = NewRegistration();
        var registered = await client.PostAsJsonAsync("/api/auth/register", registration);
        var session = await registered.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await response.Content.ReadJsonAsync<SessionUser>();

        Assert.Equal(registration.Email, user!.Email);
        Assert.Equal(UserRole.Requester, user.Role);
    }

    [Fact]
    public async Task Me_reflete_o_perfil_atual_do_banco_e_nao_o_do_token()
    {
        // O token dura quinze minutos. Se o gestor promover alguém a técnico, a próxima
        // consulta de sessão precisa mostrar o perfil novo — senão a interface continua
        // escondendo o painel técnico de quem já é técnico.
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var registration = NewRegistration();
        var registered = await client.PostAsJsonAsync("/api/auth/register", registration);
        var session = await registered.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();

        await using var db = fixture.CreateContext();
        await db.Users
            .Where(u => u.Email == registration.Email)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Role, UserRole.Technician));

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var response = await client.GetAsync("/api/auth/me");
        var user = await response.Content.ReadJsonAsync<SessionUser>();

        Assert.Equal(UserRole.Technician, user!.Role);
    }

    [Fact]
    public async Task Token_de_usuario_desativado_deixa_de_dar_acesso()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        var registration = NewRegistration();
        var registered = await client.PostAsJsonAsync("/api/auth/register", registration);
        var session = await registered.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();

        await using var db = fixture.CreateContext();
        await db.Users
            .Where(u => u.Email == registration.Email)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_com_assinatura_de_outra_chave_e_recusado()
    {
        await fixture.ResetAsync();
        using var client = fixture.Api.CreateBrowserClient();

        // Token bem formado, assinado com outra chave.
        const string forged =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJzdWIiOiIwMTkyYzNkNC01Njc4LTdhYmMtOWRlZi0wMTIzNDU2Nzg5YWIiLCJyb2xlIjoiTWFuYWdlciJ9." +
            "assinatura-invalida";

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", forged);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ----- Apoio -----

    private async Task<RegisterRequest> RegisterAsync()
    {
        using var client = fixture.Api.CreateBrowserClient();

        var registration = NewRegistration();
        var response = await client.PostAsJsonAsync("/api/auth/register", registration);

        response.EnsureSuccessStatusCode();

        return registration with { Email = registration.Email.Trim().ToLowerInvariant() };
    }

    /// <summary>Renovação apresentando um token específico, sem depender de cookie guardado.</summary>
    private async Task<HttpResponseMessage> Refresh(string? token)
    {
        using var client = fixture.Api.CreateDefaultClient();
        client.BaseAddress = new Uri("https://localhost");
        client.DefaultRequestHeaders.Add("Cookie", $"opsdesk_refresh={token}");

        return await client.PostAsync("/api/auth/refresh", null);
    }

    private static string? SentCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values
                .FirstOrDefault(c => c.StartsWith("opsdesk_refresh="))
                ?.Split(';')[0]
                .Split('=', 2)[1]
            : null;

    private static async Task<string?> Detail(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadJsonAsync<ProblemResponse>();

        return problem?.Detail;
    }

    private record ProblemResponse(string? Title, string? Detail, int? Status);

    private record ValidationProblem(Dictionary<string, string[]> Errors);
}
