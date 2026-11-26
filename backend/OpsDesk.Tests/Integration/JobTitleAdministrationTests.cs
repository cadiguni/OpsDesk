using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Api.Endpoints;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Common;
using OpsDesk.Application.JobTitles;
using OpsDesk.Application.Users;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Catálogo de cargos e o cargo de cada pessoa.
///
/// O que importa é que cargo <b>não</b> é permissão: o técnico com cargo de Tech Lead
/// continua sem administrar nada, e trocar o cargo não mexe no perfil.
/// </summary>
[Collection(PostgresCollection.Name)]
public class JobTitleAdministrationTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    // ----- Acesso -----

    [Fact]
    public async Task Tecnico_e_solicitante_nao_administram_cargos()
    {
        var world = await SetUpAsync();
        var analyst = await AddJobTitleAsync("Analista");

        foreach (var client in new[] { world.Technician, world.Requester })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/job-titles")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/job-titles/options")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await CreateAsync(client, "Tech Lead")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await RenameAsync(client, analyst, "Analista Sênior")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ChangeActivationAsync(client, analyst, false)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await ChangeUserJobTitleAsync(client, world.TechnicianId, analyst)).StatusCode);
        }

        await using var db = Fixture.CreateContext();
        Assert.Equal("Analista", (await db.JobTitles.SingleAsync(j => j.Id == analyst)).Name);
        Assert.Null((await db.Users.SingleAsync(u => u.Id == world.TechnicianId)).JobTitleId);
    }

    [Fact]
    public async Task Administrador_com_senha_provisoria_nao_chega_aos_cargos()
    {
        await Fixture.ResetAsync();

        var client = await SignInWithTemporaryPasswordAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/job-titles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateAsync(client, "Tech Lead")).StatusCode);
    }

    [Fact]
    public async Task Cargo_nao_da_permissao()
    {
        var world = await SetUpAsync();
        var techLead = await AddJobTitleAsync("Tech Lead");

        var response = await ChangeUserJobTitleAsync(world.Manager, world.TechnicianId, techLead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var changed = (await response.Content.ReadJsonAsync<ManagedUser>())!;
        Assert.Equal("Tech Lead", changed.JobTitleName);
        Assert.Equal(UserRole.Technician, changed.Role);

        // O token do técnico não mudou, e o banco também não deu nada a ele.
        Assert.Equal(HttpStatusCode.Forbidden, (await world.Technician.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await world.Technician.GetAsync("/api/dashboard")).StatusCode);
    }

    // ----- Catálogo -----

    [Theory]
    [InlineData("Analista")]
    [InlineData("analista")]
    [InlineData(" ANALISTA ")]
    public async Task Nome_repetido_e_recusado_sem_diferenciar_maiusculas(string name)
    {
        var world = await SetUpAsync();
        await AddJobTitleAsync("Analista");

        Assert.Equal(HttpStatusCode.Conflict, (await CreateAsync(world.Manager, name)).StatusCode);

        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.JobTitles.CountAsync(j => j.Name.ToLower() == "analista"));
    }

    [Fact]
    public async Task Cargo_criado_entra_no_seletor_e_desativado_sai()
    {
        var world = await SetUpAsync();

        var response = await CreateAsync(world.Manager, "  Aprendiz  ");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = (await response.Content.ReadJsonAsync<ManagedJobTitle>())!;
        Assert.Equal("Aprendiz", created.Name);
        Assert.Contains(await OptionsAsync(world.Manager), o => o.Id == created.Id);

        Assert.Equal(HttpStatusCode.OK, (await ChangeActivationAsync(world.Manager, created.Id, false)).StatusCode);
        Assert.DoesNotContain(await OptionsAsync(world.Manager), o => o.Id == created.Id);
    }

    // ----- Cargo da pessoa -----

    [Fact]
    public async Task Cargo_desativado_nao_se_atribui_mas_quem_tem_continua_com_ele()
    {
        var world = await SetUpAsync();
        var intern = await AddJobTitleAsync("Estagiário");

        Assert.Equal(HttpStatusCode.OK, (await ChangeUserJobTitleAsync(world.Manager, world.TechnicianId, intern)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ChangeActivationAsync(world.Manager, intern, false)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await ChangeUserJobTitleAsync(world.Manager, world.OtherTechnicianId, intern)).StatusCode);

        await using var db = Fixture.CreateContext();
        Assert.Equal(intern, (await db.Users.SingleAsync(u => u.Id == world.TechnicianId)).JobTitleId);
        Assert.Null((await db.Users.SingleAsync(u => u.Id == world.OtherTechnicianId)).JobTitleId);
    }

    [Fact]
    public async Task Cargo_inexistente_e_recusado_no_cadastro()
    {
        var world = await SetUpAsync();

        var response = await world.Manager.PostAsJsonAsync(
            "/api/admin/users",
            new CreateUserRequest("Pessoa Nova", "nova@empresa.com", UserRole.Technician, Guid.NewGuid()),
            TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = Fixture.CreateContext();
        Assert.False(await db.Users.AnyAsync(u => u.Email == "nova@empresa.com"));
    }

    [Fact]
    public async Task Cadastro_com_cargo_aparece_na_listagem()
    {
        var world = await SetUpAsync();
        var analyst = await AddJobTitleAsync("Analista");

        var response = await world.Manager.PostAsJsonAsync(
            "/api/admin/users",
            new CreateUserRequest("Pessoa Nova", "nova@empresa.com", UserRole.Technician, analyst),
            TestJson.Options);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var list = await world.Manager.GetAsync("/api/admin/users?search=nova@empresa.com");
        var users = (await list.Content.ReadJsonAsync<PagedResult<ManagedUser>>())!;

        var created = Assert.Single(users.Items);
        Assert.Equal(analyst, created.JobTitleId);
        Assert.Equal("Analista", created.JobTitleName);
    }

    /// <summary>
    /// Perfil e ativação da própria conta são recusados para que sempre sobre um
    /// administrador. Cargo não tem esse risco, e o administrador escolhe o próprio.
    /// </summary>
    [Fact]
    public async Task Administrador_troca_o_proprio_cargo()
    {
        var world = await SetUpAsync();
        var techLead = await AddJobTitleAsync("Tech Lead");

        Assert.Equal(HttpStatusCode.OK, (await ChangeUserJobTitleAsync(world.Manager, world.ManagerId, techLead)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ChangeUserJobTitleAsync(world.Manager, world.ManagerId, null)).StatusCode);

        await using var db = Fixture.CreateContext();
        var me = await db.Users.SingleAsync(u => u.Id == world.ManagerId);
        Assert.Null(me.JobTitleId);
        Assert.Equal(UserRole.Manager, me.Role);
    }

    [Fact]
    public async Task Trilha_guarda_o_nome_do_cargo_da_epoca()
    {
        var world = await SetUpAsync();
        var analyst = await AddJobTitleAsync("Analista");
        var techLead = await AddJobTitleAsync("Tech Lead");

        await ChangeUserJobTitleAsync(world.Manager, world.TechnicianId, analyst);
        await ChangeUserJobTitleAsync(world.Manager, world.TechnicianId, techLead);

        // Renomear depois não reescreve o passado.
        await RenameAsync(world.Manager, analyst, "Analista Sênior");

        var response = await world.Manager.GetAsync($"/api/admin/users/{world.TechnicianId}/history");
        var history = (await response.Content.ReadJsonAsync<List<UserAuditItem>>())!
            .Where(h => h.Action == UserAuditAction.JobTitleChanged)
            .OrderBy(h => h.CreatedAt)
            .ToList();

        Assert.Equal(2, history.Count);
        Assert.Null(history[0].PreviousValue);
        Assert.Equal("Analista", history[0].NewValue);
        Assert.Equal("Analista", history[1].PreviousValue);
        Assert.Equal("Tech Lead", history[1].NewValue);
        Assert.All(history, h => Assert.NotNull(h.ChangedByName));
    }

    // ----- Atalhos -----

    private async Task<Guid> AddJobTitleAsync(string name)
    {
        await using var db = Fixture.CreateContext();

        var jobTitle = new JobTitle { Name = name };
        db.JobTitles.Add(jobTitle);
        await db.SaveChangesAsync();

        return jobTitle.Id;
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string name) =>
        client.PostAsJsonAsync("/api/admin/job-titles", new SaveJobTitleRequest(name));

    private static Task<HttpResponseMessage> RenameAsync(HttpClient client, Guid id, string name) =>
        client.PutAsJsonAsync($"/api/admin/job-titles/{id}", new SaveJobTitleRequest(name));

    private static Task<HttpResponseMessage> ChangeActivationAsync(HttpClient client, Guid id, bool isActive) =>
        client.PostAsJsonAsync($"/api/admin/job-titles/{id}/activation", new ChangeJobTitleActivationRequest(isActive));

    private static Task<HttpResponseMessage> ChangeUserJobTitleAsync(HttpClient client, Guid userId, Guid? jobTitleId) =>
        client.PostAsJsonAsync($"/api/admin/users/{userId}/job-title", new ChangeJobTitleRequest(jobTitleId));

    private static async Task<List<JobTitleOption>> OptionsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/admin/job-titles/options");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<List<JobTitleOption>>())!;
    }

    private async Task<HttpClient> SignInWithTemporaryPasswordAsync()
    {
        var email = $"provisorio.{Guid.NewGuid():N}@empresa.com";

        await using var db = Fixture.CreateContext();

        var user = new User { Name = "Administrador provisório", Email = email, Role = UserRole.Manager, MustChangePassword = true };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var client = Fixture.Api.CreateBrowserClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();

        var session = await login.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);

        return client;
    }
}
