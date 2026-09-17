using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Sobe a API em memória apontando para o PostgreSQL do Testcontainers.
///
/// O ambiente é <c>Testing</c>, e não <c>Development</c>, para que as tarefas de
/// inicialização não rodem: migration e seed são responsabilidade da fixture, que precisa
/// controlar exatamente o que existe no banco em cada teste.
/// </summary>
public class OpsDeskApiFactory(
    string connectionString,
    int credentialAttemptsPerMinute = 10_000,
    int refreshAttemptsPerMinute = 10_000,
    int refreshGraceSeconds = 30) : WebApplicationFactory<Program>
{
    /// <summary>
    /// Chave de assinatura só deste processo de teste. Não reaproveitamos a de
    /// desenvolvimento para que um teste não passe por acidente contra token emitido fora.
    /// </summary>
    private const string SigningKey = "chave-de-assinatura-exclusiva-dos-testes-de-integracao";

    /// <summary>
    /// Raiz dos anexos deste processo de teste. Pasta temporária própria: os testes não
    /// devem sujar o diretório de trabalho nem enxergar arquivo de uma execução anterior.
    /// </summary>
    private readonly string _attachmentRoot =
        Path.Combine(Path.GetTempPath(), $"opsdesk-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:OpsDesk"] = connectionString,
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:Issuer"] = "opsdesk-tests",
                ["Jwt:Audience"] = "opsdesk-tests",
                ["Cors:AllowedOrigins:0"] = "https://localhost",
                ["AttachmentStorage:RootPath"] = _attachmentRoot,

                // Os limites reais são vinte tentativas de credencial e cento e vinte
                // renovações por minuto, por IP. No TestServer todas as requisições vêm sem
                // endereço de origem, portanto caem na mesma partição: com os valores de
                // produção, a suíte se estrangularia sozinha e os testes passariam a
                // depender da ordem de execução. Os testes de rate limiting sobem fábricas
                // próprias com limite baixo.
                ["RateLimiting:CredentialAttemptsPerMinute"] =
                    credentialAttemptsPerMinute.ToString(),
                ["RateLimiting:RefreshAttemptsPerMinute"] =
                    refreshAttemptsPerMinute.ToString(),
                ["Jwt:RefreshTokenGraceSeconds"] = refreshGraceSeconds.ToString(),
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_attachmentRoot))
        {
            Directory.Delete(_attachmentRoot, recursive: true);
        }
    }

    /// <summary>
    /// Cliente que guarda cookie entre requisições, como um navegador.
    ///
    /// A base é <c>https</c> de propósito: o cookie de refresh é marcado <c>Secure</c>, e
    /// um <see cref="CookieContainer"/> se recusa a devolvê-lo em requisição <c>http</c>.
    /// Com base <c>http</c> o teste de renovação passaria a testar o nada. O TestServer
    /// não faz TLS de verdade, então o esquema aqui é só o que o cookie precisa ver.
    /// </summary>
    public HttpClient CreateBrowserClient()
    {
        var cookies = new CookieHandler();

        var client = CreateDefaultClient(cookies);
        client.BaseAddress = new Uri("https://localhost");

        return client;
    }
}

/// <summary>
/// Persiste cookie entre requisições do <see cref="HttpClient"/> de teste.
///
/// O transporte do TestServer é em memória, então o <c>HttpClientHandler</c> — e com ele
/// o suporte nativo a cookie — não entra no caminho. Este handler devolve esse
/// comportamento, respeitando <c>Path</c>, <c>Secure</c> e expiração.
/// </summary>
public class CookieHandler : DelegatingHandler
{
    private readonly CookieContainer _cookies = new();

    public string? this[string name]
    {
        get
        {
            var cookie = _cookies
                .GetCookies(new Uri("https://localhost/api/auth"))
                .FirstOrDefault(c => c.Name == name);

            return cookie?.Value;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;

        var header = _cookies.GetCookieHeader(uri);

        if (!string.IsNullOrEmpty(header))
        {
            request.Headers.Add("Cookie", header);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                _cookies.SetCookies(uri, setCookie);
            }
        }

        return response;
    }
}
