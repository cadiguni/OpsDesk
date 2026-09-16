using System.Net;
using System.Net.Http.Json;
using OpsDesk.Application.Auth;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// O rate limiting do login, verificado com uma API própria de limite baixo.
///
/// A fábrica compartilhada usa um limite altíssimo, porque no TestServer todas as
/// requisições caem na mesma partição e o limite de produção estrangularia a suíte
/// inteira. Aqui o limite baixo é o objeto do teste, e não um estorvo.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AuthRateLimitTests(PostgresFixture fixture)
{
    private const int Limit = 3;

    [Fact]
    public async Task Login_repetido_passa_a_receber_429()
    {
        await fixture.ResetAsync();

        await using var api = new OpsDeskApiFactory(
            fixture.ConnectionString, anonymousPermitsPerMinute: Limit);

        using var client = api.CreateBrowserClient();

        var attempt = new LoginRequest("ninguem@empresa.com", "senha-qualquer-123");
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < Limit + 2; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", attempt);
            statuses.Add(response.StatusCode);
        }

        // As primeiras tentativas são recusadas por credencial; as que passam do limite
        // são recusadas antes de chegar ao serviço.
        Assert.All(statuses.Take(Limit), status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.All(statuses.Skip(Limit), status => Assert.Equal(HttpStatusCode.TooManyRequests, status));
    }

    [Fact]
    public async Task O_limite_nao_alcanca_endpoint_autenticado()
    {
        // /me é do usuário já autenticado e não entra na cota anônima. Se entrasse, uma
        // interface que consulta a sessão a cada navegação se autobloquearia.
        await fixture.ResetAsync();

        await using var api = new OpsDeskApiFactory(
            fixture.ConnectionString, anonymousPermitsPerMinute: Limit);

        using var client = api.CreateBrowserClient();

        var registration = new RegisterRequest(
            "Ana Paula", "ana.rate@empresa.com", "senha-de-teste-123", "senha-de-teste-123");

        var registered = await client.PostAsJsonAsync("/api/auth/register", registration);
        var session = await registered.Content.ReadJsonAsync<Api.Endpoints.AuthEndpoints.AuthResponse>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);

        for (var i = 0; i < Limit + 3; i++)
        {
            var response = await client.GetAsync("/api/auth/me");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
