using System.Net;
using System.Net.Http.Json;
using OpsDesk.Application.Auth;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// A cota de login por IP atrás de proxy. Sem a confiança configurada, todo mundo que
/// passa pelo mesmo proxy divide uma cota; com ela, cada cliente tem a sua — e um
/// cliente fora da rede confiável não consegue escolher a própria partição.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ProxyTrustTests(PostgresFixture fixture)
{
    private const int Limit = 2;
    private const string Proxy = "172.28.0.10";

    [Fact]
    public async Task Atras_de_proxy_confiavel_cada_cliente_tem_a_propria_cota()
    {
        await fixture.ResetAsync();

        await using var api = Api(trustedNetwork: "172.28.0.0/16");

        // O cliente A esgota a cota dele...
        for (var i = 0; i < Limit; i++)
        {
            await LoginAsync(api, client: "203.0.113.1");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(api, client: "203.0.113.1")).StatusCode);

        // ...e o cliente B, atrás do mesmo proxy, continua podendo tentar.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(api, client: "203.0.113.2")).StatusCode);
    }

    [Fact]
    public async Task Proxy_fora_da_rede_confiavel_nao_escolhe_a_particao()
    {
        await fixture.ResetAsync();

        // Nenhuma rede confiável: o cabeçalho é ignorado e vale o IP da conexão.
        await using var api = Api(trustedNetwork: null);

        for (var i = 0; i < Limit; i++)
        {
            await LoginAsync(api, client: $"203.0.113.{i + 10}");
        }

        // Trocar o X-Forwarded-For não dá cota nova.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(api, client: "203.0.113.99")).StatusCode);
    }

    private OpsDeskApiFactory Api(string? trustedNetwork) => new(
        fixture.ConnectionString,
        credentialAttemptsPerMinute: Limit,
        extraSettings: trustedNetwork is null
            ? null
            : new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks:0"] = trustedNetwork });

    private static async Task<HttpResponseMessage> LoginAsync(OpsDeskApiFactory api, string client)
    {
        using var http = api.CreateBrowserClient();

        http.DefaultRequestHeaders.Add(TestConnectionIpFilter.Header, Proxy);
        http.DefaultRequestHeaders.Add("X-Forwarded-For", client);

        return await http.PostAsJsonAsync("/api/auth/login", new LoginRequest("ninguem@empresa.com", "senha-qualquer-123"));
    }
}
