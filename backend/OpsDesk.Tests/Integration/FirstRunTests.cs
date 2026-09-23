using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Api.Endpoints;
using OpsDesk.Application.Auth;
using OpsDesk.Domain.Enums;
using OpsDesk.Infrastructure.Persistence.Seed;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Instalação em branco: o bootstrap do primeiro gestor e a senha que vale uma vez.
///
/// O que estes testes protegem é o caminho que o ambiente de desenvolvimento esconde.
/// Em Development existe um gestor de seed e nada disso é exercitado — é exatamente por
/// isso que o problema só apareceria na primeira instalação de verdade.
/// </summary>
[Collection(PostgresCollection.Name)]
public class FirstRunTests(PostgresFixture fixture)
{
    private const string BootstrapPassword = "senha-de-bootstrap-123";

    private const string BootstrapEmail = "gestor@empresa.com";

    private const string PasswordChangeRequiredType =
        "https://opsdesk.local/errors/password-change-required";

    private static BootstrapAdminOptions Bootstrap(string? email = null) => new()
    {
        Email = email ?? BootstrapEmail,
        Password = BootstrapPassword,
        Name = "Gestora de Suporte"
    };

    // ----- Bootstrap do primeiro gestor -----

    [Fact]
    public async Task Bootstrap_cria_o_primeiro_gestor_com_senha_provisoria()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var result = await TestData.NewSeeder(db, Bootstrap()).EnsureBootstrapAdminAsync();

        Assert.Equal(BootstrapAdminResult.Created, result);

        var manager = await db.Users.SingleAsync();

        Assert.Equal(UserRole.Manager, manager.Role);
        Assert.Equal(BootstrapEmail, manager.Email);

        // A senha veio por variável de ambiente, que é canal que registra. Nascer sem esta
        // trava transformaria a credencial de instalação em credencial permanente.
        Assert.True(manager.MustChangePassword);
    }

    [Fact]
    public async Task Bootstrap_normaliza_o_email_como_o_login_faz()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        await TestData.NewSeeder(db, Bootstrap("  Gestor@Empresa.COM  ")).EnsureBootstrapAdminAsync();

        // Sem isto, o e-mail configurado com maiúscula criaria uma conta que o login,
        // que compara em minúsculas, nunca encontraria.
        Assert.Equal(BootstrapEmail, (await db.Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task Bootstrap_nao_faz_nada_sem_configuracao()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var result = await TestData.NewSeeder(db).EnsureBootstrapAdminAsync();

        Assert.Equal(BootstrapAdminResult.NotConfigured, result);
        Assert.False(await db.Users.AnyAsync());
    }

    [Fact]
    public async Task Bootstrap_nao_recria_gestor_em_sistema_ja_instalado()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        db.Users.Add(TestData.NewUser(UserRole.Manager, "outra.gestora@empresa.com"));
        await db.SaveChangesAsync();

        var result = await TestData.NewSeeder(db, Bootstrap()).EnsureBootstrapAdminAsync();

        // A variável esquecida no orquestrador não pode ressuscitar uma conta
        // administrativa a cada deploy — muito menos uma que alguém desativou de propósito.
        Assert.Equal(BootstrapAdminResult.ManagerAlreadyExists, result);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Bootstrap_promove_conta_existente_em_vez_de_colidir()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        db.Users.Add(TestData.NewUser(UserRole.Requester, BootstrapEmail));
        await db.SaveChangesAsync();

        var result = await TestData.NewSeeder(db, Bootstrap()).EnsureBootstrapAdminAsync();

        Assert.Equal(BootstrapAdminResult.Promoted, result);

        var promoted = await db.Users.SingleAsync();

        Assert.Equal(UserRole.Manager, promoted.Role);

        // A pessoa já tinha senha própria: a configurada é ignorada, e por isso não há
        // credencial provisória a trocar.
        Assert.False(promoted.MustChangePassword);
    }

    // ----- A senha provisória vale para uma coisa só -----

    [Fact]
    public async Task Senha_provisoria_recusa_tudo_fora_da_autenticacao()
    {
        using var client = await BootstrappedClientAsync();

        var response = await client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // A interface precisa distinguir isto de falta de permissão: uma tem saída, a
        // outra é uma porta fechada.
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();

        Assert.Equal(PasswordChangeRequiredType, problem?.Type);
    }

    [Fact]
    public async Task Senha_provisoria_aparece_no_me()
    {
        using var client = await BootstrappedClientAsync();

        var me = await client.GetFromJsonAsync<SessionUser>("/api/auth/me");

        Assert.True(me?.MustChangePassword);
    }

    [Fact]
    public async Task Troca_de_senha_libera_o_sistema()
    {
        using var client = await BootstrappedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/password",
            new ChangePasswordRequest(BootstrapPassword, "nova-senha-escolhida", "nova-senha-escolhida"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();

        Assert.False(body!.User.MustChangePassword);

        // O token reemitido é o que a interface passa a usar; sem ele a pessoa teria de
        // entrar de novo logo depois de provar quem é.
        client.DefaultRequestHeaders.Authorization = new("Bearer", body.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories")).StatusCode);
    }

    [Fact]
    public async Task Troca_de_senha_exige_a_senha_atual()
    {
        using var client = await BootstrappedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/password",
            new ChangePasswordRequest("nao-e-a-senha-atual", "nova-senha-escolhida", "nova-senha-escolhida"));

        // Token de acesso em máquina deixada aberta não pode bastar para assumir a conta.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Troca_de_senha_recusa_repetir_a_provisoria()
    {
        using var client = await BootstrappedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/password",
            new ChangePasswordRequest(BootstrapPassword, BootstrapPassword, BootstrapPassword));

        // Aceitar deixaria em pé exatamente a credencial que a troca existe para aposentar.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = fixture.CreateContext();

        Assert.True((await db.Users.SingleAsync()).MustChangePassword);
    }

    [Fact]
    public async Task Troca_de_senha_derruba_as_outras_sessoes()
    {
        using var first = await BootstrappedClientAsync();

        // Uma segunda sessão da mesma conta, aberta antes da troca.
        using var second = fixture.Api.CreateBrowserClient();
        await second.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(BootstrapEmail, BootstrapPassword));

        await first.PostAsJsonAsync(
            "/api/auth/password",
            new ChangePasswordRequest(BootstrapPassword, "nova-senha-escolhida", "nova-senha-escolhida"));

        var refreshed = await second.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
    }

    private record ProblemBody(string? Type);

    /// <summary>
    /// Banco recém-instalado, gestor criado pelo bootstrap, e um cliente já autenticado
    /// com a senha provisória.
    /// </summary>
    private async Task<HttpClient> BootstrappedClientAsync()
    {
        await fixture.ResetAsync();

        await using (var db = fixture.CreateContext())
        {
            // Os dados de referência entram porque o teste que atravessa a trava precisa
            // de um endpoint que responderia 200 do outro lado dela.
            var seeder = TestData.NewSeeder(db, Bootstrap());

            await seeder.SeedAsync(includeDevelopmentUsers: false);
            await seeder.EnsureBootstrapAdminAsync();
        }

        var client = fixture.Api.CreateBrowserClient();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(BootstrapEmail, BootstrapPassword));

        var session = await login.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();

        client.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);

        return client;
    }
}
