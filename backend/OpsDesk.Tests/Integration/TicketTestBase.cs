using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpsDesk.Api.Endpoints;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Common;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Infrastructure.Persistence.Seed;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Cenário e atalhos compartilhados pelos testes de chamado.
///
/// Monta um mundo com um cliente HTTP autenticado por perfil — dois solicitantes, dois
/// técnicos e um gestor — porque quase todo caso de autorização precisa comparar "o meu"
/// com "o de outra pessoa", e repetir essa montagem em cada arquivo faria a diferença
/// entre os cenários sumir no meio do código de preparação.
/// </summary>
public abstract class TicketTestBase(PostgresFixture fixture)
{
    protected const string Password = "senha-de-teste-123";

    protected PostgresFixture Fixture { get; } = fixture;

    protected static CreateTicketRequest NewTicket(
        Guid categoryId,
        string title = "Impressora não imprime",
        TicketPriority priority = TicketPriority.Medium) => new(
        title, "O equipamento liga mas não puxa papel.", categoryId, priority);

    /// <summary>Banco limpo, dados de referência do seed e um cliente por perfil.</summary>
    protected async Task<World> SetUpAsync()
    {
        await Fixture.ResetAsync();

        await using var db = Fixture.CreateContext();

        // Categorias e políticas de SLA são dados de referência: sem eles não há como
        // abrir chamado. O seed é o mesmo que a API roda em desenvolvimento.
        var seeder = new DatabaseSeeder(
            db, new PasswordHasher<User>(), NullLogger<DatabaseSeeder>.Instance);
        await seeder.SeedAsync(includeDevelopmentUsers: false);

        var categoryId = await db.Categories
            .Where(c => c.Name == "VPN")
            .Select(c => c.Id)
            .SingleAsync();

        var (requester, requesterId) = await SignInAsync(UserRole.Requester);
        var (otherRequester, otherRequesterId) = await SignInAsync(UserRole.Requester);
        var (technician, technicianId) = await SignInAsync(UserRole.Technician);
        var (otherTechnician, otherTechnicianId) = await SignInAsync(UserRole.Technician);
        var (manager, managerId) = await SignInAsync(UserRole.Manager);

        return new World(
            requester, requesterId,
            otherRequester, otherRequesterId,
            technician, technicianId,
            otherTechnician, otherTechnicianId,
            manager, managerId,
            categoryId);
    }

    /// <summary>Abre um chamado. Por padrão como o solicitante principal.</summary>
    protected async Task<Guid> OpenTicketAsync(
        World world,
        string title = "Não consigo acessar a VPN",
        TicketPriority priority = TicketPriority.Medium,
        HttpClient? client = null)
    {
        var response = await (client ?? world.Requester).PostAsJsonAsync(
            "/api/tickets", NewTicket(world.CategoryId, title, priority));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<TicketDetail>())!.Id;
    }

    protected static async Task<TicketDetail> GetAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/tickets/{id}");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<TicketDetail>())!;
    }

    protected static async Task<PagedResult<TicketListItem>> ListAsync(
        HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/tickets{query}");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<PagedResult<TicketListItem>>())!;
    }

    protected static async Task<TicketDetail> ChangeStatusAsync(
        HttpClient client, Guid id, TicketStatus status)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{id}/status", new ChangeStatusRequest(status));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<TicketDetail>())!;
    }

    protected static async Task<TicketDetail> AssignAsync(
        HttpClient client, Guid id, Guid? technicianId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{id}/assignment", new AssignRequest(technicianId));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<TicketDetail>())!;
    }

    protected static async Task<List<TicketCommentItem>> CommentsAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/tickets/{id}/comments");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<List<TicketCommentItem>>())!;
    }

    protected static async Task<List<TicketHistoryItem>> HistoryAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/tickets/{id}/history");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<List<TicketHistoryItem>>())!;
    }

    /// <summary>Cria um usuário do perfil pedido e devolve um cliente já autenticado.</summary>
    protected async Task<(HttpClient Client, Guid UserId)> SignInAsync(UserRole role)
    {
        var email = $"{role}.{Guid.NewGuid():N}@empresa.com".ToLowerInvariant();

        await using var db = Fixture.CreateContext();

        var hasher = new PasswordHasher<User>();
        var user = new User { Name = $"Usuário {role}", Email = email, Role = role };
        user.PasswordHash = hasher.HashPassword(user, Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var client = Fixture.Api.CreateBrowserClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();

        var session = await login.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return (client, user.Id);
    }

    protected record World(
        HttpClient Requester,
        Guid RequesterId,
        HttpClient OtherRequester,
        Guid OtherRequesterId,
        HttpClient Technician,
        Guid TechnicianId,
        HttpClient OtherTechnician,
        Guid OtherTechnicianId,
        HttpClient Manager,
        Guid ManagerId,
        Guid CategoryId)
    {
        public Guid OwnTicketId { get; init; }

        public Guid UnassignedTicketId { get; init; }

        public Guid OfOtherTechnicianId { get; init; }
    }
}
