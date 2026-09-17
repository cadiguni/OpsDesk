using System.Net;
using System.Net.Http.Json;
using OpsDesk.Application.Auth;
using OpsDesk.Domain.Enums;

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
            fixture.ConnectionString, credentialAttemptsPerMinute: Limit);

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
    public async Task A_renovacao_de_sessao_nao_consome_a_cota_de_login()
    {
        // A falha que este teste fixa: com cota compartilhada, recarregar a página algumas
        // vezes gastava as tentativas de credencial, e o usuário legítimo recebia "muitas
        // tentativas" na primeira vez que digitava a senha, sem nunca ter errado nada.
        await fixture.ResetAsync();

        await using var api = new OpsDeskApiFactory(
            fixture.ConnectionString,
            credentialAttemptsPerMinute: Limit,
            refreshAttemptsPerMinute: 100);

        using var client = api.CreateBrowserClient();

        var registration = new RegisterRequest(
            "Bruno Lima", "bruno.cota@empresa.com", "senha-de-teste-123", "senha-de-teste-123");

        await client.PostAsJsonAsync("/api/auth/register", registration);

        // Muito mais renovações do que a cota de credencial permitiria.
        for (var i = 0; i < Limit * 3; i++)
        {
            var refresh = await client.PostAsync("/api/auth/refresh", null);

            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        }

        // E o login continua disponível.
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(registration.Email, "senha-de-teste-123"));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task O_limite_nao_alcanca_endpoint_autenticado()
    {
        // /me é do usuário já autenticado e não entra na cota anônima. Se entrasse, uma
        // interface que consulta a sessão a cada navegação se autobloquearia.
        await fixture.ResetAsync();

        await using var api = new OpsDeskApiFactory(
            fixture.ConnectionString, credentialAttemptsPerMinute: Limit);

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

/// <summary>
/// O rate limiting do envio de anexo.
///
/// Aqui a cota não é contra força bruta, é contra consumo: cada envio aceito grava até dez
/// megabytes. E a partição é por <b>usuário</b>, não por IP — o que só funciona porque a
/// autenticação roda antes do limitador no pipeline. Este teste existe sobretudo por causa
/// disso: com a ordem invertida, a claim ainda não existe, todo mundo cai na partição de
/// fallback por IP, e um escritório atrás de NAT passa a dividir uma cota só.
/// </summary>
[Collection(PostgresCollection.Name)]
public class UploadRateLimitTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    private const int Limit = 3;

    [Fact]
    public async Task Envio_repetido_passa_a_receber_429_e_a_cota_e_por_usuario()
    {
        await Fixture.ResetAsync();

        await using var api = new OpsDeskApiFactory(Fixture.ConnectionString, uploadsPerMinute: Limit);

        var (primeiro, _) = await SignInAsync(api, UserRole.Requester);
        var (segundo, _) = await SignInAsync(api, UserRole.Requester);

        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < Limit + 1; i++)
        {
            statuses.Add((await PostFileAsync(primeiro, $"print-{i}.png")).StatusCode);
        }

        Assert.All(
            statuses.Take(Limit),
            status => Assert.Equal(HttpStatusCode.Created, status));

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);

        // O segundo usuário tem a própria cota: quem estourou o limite não tranca o colega.
        var doOutro = await PostFileAsync(segundo, "print-do-outro.png");
        Assert.Equal(HttpStatusCode.Created, doOutro.StatusCode);

        primeiro.Dispose();
        segundo.Dispose();
    }

    private static async Task<HttpResponseMessage> PostFileAsync(HttpClient client, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3, 4]);
        file.Headers.TryAddWithoutValidation("Content-Type", "image/png");
        form.Add(file, "file", fileName);

        return await client.PostAsync("/api/attachments", form);
    }
}
