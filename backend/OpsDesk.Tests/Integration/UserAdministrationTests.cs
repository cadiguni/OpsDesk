using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Api.Endpoints;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Common;
using OpsDesk.Application.Users;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Administração de usuários pelo gestor.
///
/// Os casos que importam são os negativos: quem não é gestor não administra, o gestor não
/// se tira do posto, e ninguém sai da equipe deixando chamado em aberto no nome. E o que
/// acontece com a sessão de quem perdeu acesso — é por ela que a mudança vale de verdade.
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserAdministrationTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    private const string PasswordChangeRequiredType =
        "https://opsdesk.local/errors/password-change-required";

    // ----- Acesso -----

    [Fact]
    public async Task Tecnico_e_solicitante_nao_administram_usuarios()
    {
        var world = await SetUpAsync();

        foreach (var client in new[] { world.Technician, world.Requester })
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/admin/users")).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden,
                (await ChangeRoleAsync(client, world.OtherRequesterId, UserRole.Manager)).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden,
                (await ChangeActivationAsync(client, world.OtherRequesterId, false)).StatusCode);
        }

        await using var db = Fixture.CreateContext();
        var target = await db.Users.SingleAsync(u => u.Id == world.OtherRequesterId);

        Assert.Equal(UserRole.Requester, target.Role);
        Assert.True(target.IsActive);
    }

    [Fact]
    public async Task Gestor_com_senha_provisoria_nao_administra_usuarios()
    {
        await Fixture.ResetAsync();

        var client = await SignInWithTemporaryPasswordAsync(UserRole.Manager);

        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal(PasswordChangeRequiredType, problem?.Type);
    }

    [Fact]
    public async Task Gestor_rebaixado_perde_a_administracao_mesmo_com_token_ainda_valido()
    {
        var world = await SetUpAsync();
        var (otherManager, otherManagerId) = await SignInAsync(UserRole.Manager);

        Assert.Equal(HttpStatusCode.OK,
            (await ChangeRoleAsync(world.Manager, otherManagerId, UserRole.Technician)).StatusCode);

        // O token do outro gestor ainda diz "Manager" e ainda não expirou. Passa pela
        // política da rota, e é o serviço, olhando o banco, que recusa.
        var response = await ChangeRoleAsync(otherManager, world.RequesterId, UserRole.Manager);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = Fixture.CreateContext();
        Assert.Equal(UserRole.Requester, (await db.Users.SingleAsync(u => u.Id == world.RequesterId)).Role);
    }

    // ----- Listagem -----

    [Fact]
    public async Task Listagem_traz_inativos_e_filtra_por_perfil_situacao_e_busca()
    {
        var world = await SetUpAsync();

        await ChangeActivationAsync(world.Manager, world.OtherRequesterId, false);

        var all = await ListUsersAsync(world.Manager);
        Assert.Equal(5, all.TotalCount);

        var inactive = await ListUsersAsync(world.Manager, "?isActive=false");
        Assert.Equal(world.OtherRequesterId, Assert.Single(inactive.Items).Id);

        var technicians = await ListUsersAsync(world.Manager, "?role=Technician");
        Assert.Equal(2, technicians.TotalCount);
        Assert.All(technicians.Items, u => Assert.Equal(UserRole.Technician, u.Role));

        var bySearch = await ListUsersAsync(world.Manager, "?search=MANAGER.");
        Assert.Equal(world.ManagerId, Assert.Single(bySearch.Items).Id);
    }

    [Fact]
    public async Task Listagem_limita_o_tamanho_da_pagina()
    {
        var world = await SetUpAsync();

        var page = await ListUsersAsync(world.Manager, "?pageSize=100000");

        Assert.Equal(PageRequest.MaxPageSize, page.PageSize);
    }

    [Fact]
    public async Task Listagem_conta_so_chamados_em_aberto_sob_responsabilidade()
    {
        var world = await SetUpAsync();

        var open = await OpenTicketAsync(world);
        var closed = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, open, world.TechnicianId);
        await AssignAsync(world.Manager, closed, world.TechnicianId);
        await ChangeStatusAsync(world.Technician, closed, TicketStatus.Cancelled);

        var technician = Assert.Single(
            (await ListUsersAsync(world.Manager, "?role=Technician")).Items,
            u => u.Id == world.TechnicianId);

        Assert.Equal(1, technician.OpenAssignedTickets);
    }

    // ----- Perfil -----

    [Fact]
    public async Task Promover_vale_na_proxima_renovacao_sem_derrubar_a_sessao()
    {
        var world = await SetUpAsync();

        var response = await ChangeRoleAsync(world.Manager, world.RequesterId, UserRole.Technician);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(UserRole.Technician, (await response.Content.ReadJsonAsync<ManagedUser>())!.Role);

        var refresh = await world.Requester.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        var session = await refresh.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();
        Assert.Equal(UserRole.Technician, session!.User.Role);

        // Com o token novo, a pessoa já enxerga o que a equipe enxerga.
        world.Requester.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await world.Requester.GetAsync("/api/staff")).StatusCode);
    }

    [Fact]
    public async Task Rebaixar_derruba_as_sessoes_da_pessoa()
    {
        var world = await SetUpAsync();

        var response = await ChangeRoleAsync(world.Manager, world.TechnicianId, UserRole.Requester);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await world.Technician.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Rebaixar_a_solicitante_com_chamado_em_aberto_e_recusado_com_a_lista()
    {
        var world = await SetUpAsync();

        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);

        var response = await ChangeRoleAsync(world.Manager, world.TechnicianId, UserRole.Requester);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadJsonAsync<BlockingProblem>();
        Assert.Equal(ticket, Assert.Single(problem!.Tickets).Id);

        await using var db = Fixture.CreateContext();
        Assert.Equal(UserRole.Technician, (await db.Users.SingleAsync(u => u.Id == world.TechnicianId)).Role);

        // A sessão também fica: a recusa não tem efeito colateral nenhum.
        Assert.Equal(HttpStatusCode.OK,
            (await world.Technician.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Tecnico_vira_gestor_mesmo_com_chamado_em_aberto()
    {
        var world = await SetUpAsync();

        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);

        var response = await ChangeRoleAsync(world.Manager, world.TechnicianId, UserRole.Manager);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Gestor_nao_altera_o_proprio_perfil()
    {
        var world = await SetUpAsync();

        var response = await ChangeRoleAsync(world.Manager, world.ManagerId, UserRole.Technician);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var db = Fixture.CreateContext();
        Assert.Equal(UserRole.Manager, (await db.Users.SingleAsync(u => u.Id == world.ManagerId)).Role);
    }

    [Fact]
    public async Task Perfil_inexistente_e_recusado()
    {
        var world = await SetUpAsync();

        var response = await world.Manager.PostAsJsonAsync(
            $"/api/admin/users/{world.RequesterId}/role", new { role = "Admin" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_inexistente_responde_404()
    {
        var world = await SetUpAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await ChangeRoleAsync(world.Manager, Guid.NewGuid(), UserRole.Technician)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ChangeActivationAsync(world.Manager, Guid.NewGuid(), false)).StatusCode);
    }

    // ----- Ativação -----

    [Fact]
    public async Task Desativar_derruba_as_sessoes_e_impede_o_login()
    {
        var world = await SetUpAsync();

        var response = await ChangeActivationAsync(world.Manager, world.TechnicianId, false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadJsonAsync<ManagedUser>())!.IsActive);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await world.Technician.PostAsync("/api/auth/refresh", null)).StatusCode);

        await using var db = Fixture.CreateContext();
        Assert.False(await db.RefreshTokens.AnyAsync(
            t => t.UserId == world.TechnicianId && t.RevokedAt == null));

        var email = (await db.Users.SingleAsync(u => u.Id == world.TechnicianId)).Email;
        using var client = Fixture.Api.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password))).StatusCode);
    }

    [Fact]
    public async Task Reativar_devolve_o_login()
    {
        var world = await SetUpAsync();

        await ChangeActivationAsync(world.Manager, world.RequesterId, false);
        var response = await ChangeActivationAsync(world.Manager, world.RequesterId, true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = Fixture.CreateContext();
        var email = (await db.Users.SingleAsync(u => u.Id == world.RequesterId)).Email;

        using var client = Fixture.Api.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password))).StatusCode);
    }

    [Fact]
    public async Task Desativar_responsavel_por_chamado_em_aberto_e_recusado_ate_reatribuir()
    {
        var world = await SetUpAsync();

        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);

        var refused = await ChangeActivationAsync(world.Manager, world.TechnicianId, false);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await refused.Content.ReadJsonAsync<BlockingProblem>();
        Assert.Equal(ticket, Assert.Single(problem!.Tickets).Id);

        await AssignAsync(world.Manager, ticket, world.OtherTechnicianId);

        Assert.Equal(HttpStatusCode.OK,
            (await ChangeActivationAsync(world.Manager, world.TechnicianId, false)).StatusCode);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved, true)]
    [InlineData(TicketStatus.Closed, false)]
    [InlineData(TicketStatus.Cancelled, false)]
    public async Task So_chamado_terminal_deixa_de_bloquear_a_desativacao(TicketStatus status, bool blocks)
    {
        var world = await SetUpAsync();

        var ticket = await OpenTicketAsync(world);
        await AssignAsync(world.Manager, ticket, world.TechnicianId);

        if (status == TicketStatus.Cancelled)
        {
            await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Cancelled);
        }
        else
        {
            await ChangeStatusAsync(world.Technician, ticket, TicketStatus.InProgress);
            await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Resolved);

            if (status == TicketStatus.Closed)
            {
                await ChangeStatusAsync(world.Technician, ticket, TicketStatus.Closed);
            }
        }

        var response = await ChangeActivationAsync(world.Manager, world.TechnicianId, false);

        Assert.Equal(blocks ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Solicitante_com_chamado_em_aberto_pode_ser_desativado()
    {
        var world = await SetUpAsync();
        await OpenTicketAsync(world);

        // O bloqueio é por responsabilidade, não por autoria: o chamado de quem saiu da
        // empresa continua na fila e continua sendo atendido.
        Assert.Equal(HttpStatusCode.OK,
            (await ChangeActivationAsync(world.Manager, world.RequesterId, false)).StatusCode);
    }

    [Fact]
    public async Task Gestor_nao_desativa_a_propria_conta()
    {
        var world = await SetUpAsync();

        var response = await ChangeActivationAsync(world.Manager, world.ManagerId, false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var db = Fixture.CreateContext();
        Assert.True((await db.Users.SingleAsync(u => u.Id == world.ManagerId)).IsActive);
    }

    // ----- Atalhos -----

    private static Task<HttpResponseMessage> ChangeRoleAsync(HttpClient client, Guid userId, UserRole role) =>
        client.PostAsJsonAsync($"/api/admin/users/{userId}/role", new ChangeRoleRequest(role), TestJson.Options);

    private static Task<HttpResponseMessage> ChangeActivationAsync(HttpClient client, Guid userId, bool isActive) =>
        client.PostAsJsonAsync($"/api/admin/users/{userId}/activation", new ChangeActivationRequest(isActive));

    private static async Task<PagedResult<ManagedUser>> ListUsersAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/admin/users{query}");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<PagedResult<ManagedUser>>())!;
    }

    private async Task<HttpClient> SignInWithTemporaryPasswordAsync(UserRole role)
    {
        var email = $"provisorio.{Guid.NewGuid():N}@empresa.com";

        await using var db = Fixture.CreateContext();

        var user = new User { Name = "Gestor provisório", Email = email, Role = role, MustChangePassword = true };
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

    private record ProblemBody(string? Type, string? Title);

    private record BlockingProblem(List<BlockingTicket> Tickets);
}
