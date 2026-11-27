using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Api.Endpoints;
using OpsDesk.Application.Auth;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// A arquibancada é escondida, mas não é aberta: pede sessão, respeita a trava de senha
/// provisória e não aparece no OpenAPI. A suíte roda sem chave da football-data.org, então
/// o caminho feliz daqui é o "não configurado" — a leitura em si está em <c>FootballDataFeedTests</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public class FunEndpointsTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    [Fact]
    public async Task Sem_sessao_nao_entra()
    {
        await Fixture.ResetAsync();

        var response = await Fixture.Api.CreateBrowserClient().GetAsync("/api/arquibancada/standings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sem_chave_configurada_responde_503_explicando()
    {
        var world = await SetUpAsync();

        foreach (var path in new[] { "/api/arquibancada/standings", "/api/arquibancada/teams/1767/matches" })
        {
            var response = await world.Requester.GetAsync(path);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("Football__ApiKey", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Senha_provisoria_trava_a_arquibancada_tambem()
    {
        await Fixture.ResetAsync();

        var email = $"provisorio.{Guid.NewGuid():N}@empresa.com";
        await using (var db = Fixture.CreateContext())
        {
            var user = new User { Name = "Provisório", Email = email, Role = UserRole.Requester, MustChangePassword = true };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = Fixture.Api.CreateBrowserClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadJsonAsync<AuthEndpoints.AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);

        var response = await client.GetAsync("/api/arquibancada/standings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void Nao_aparece_no_openapi()
    {
        // O documento OpenAPI só é publicado em Development; o que o decide é este metadado.
        var endpoints = Fixture.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/arquibancada") == true)
            .ToList();

        Assert.Equal(2, endpoints.Count);
        Assert.All(endpoints, e =>
            Assert.True(e.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription));
    }
}
