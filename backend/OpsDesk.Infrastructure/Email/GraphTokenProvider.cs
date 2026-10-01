using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using OpsDesk.Application.Abstractions;

namespace OpsDesk.Infrastructure.Email;

/// <summary>
/// Token de aplicativo do Entra ID (client credentials) para o Microsoft Graph, em cache
/// até perto de expirar. Compartilhado pelo envio e pela leitura das caixas: é o mesmo
/// aplicativo registrado, com <c>Mail.Send</c> e <c>Mail.ReadWrite</c>.
/// </summary>
public class GraphTokenProvider(HttpClient http, IMemoryCache cache)
{
    public const string LoginBaseAddress = "https://login.microsoftonline.com/";

    public async Task<string> GetAsync(EmailSenderCredentials credentials, CancellationToken cancellationToken)
    {
        // A chave inclui um hash do secret: trocar o secret na tela invalida o token em
        // cache na hora, em vez de continuar usando o antigo até ele expirar. SHA-256, e
        // não GetHashCode, para o secret não virar chave legível nem colidir por acaso.
        var secretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credentials.ClientSecret)));
        var key = $"graph-token:{credentials.TenantId}:{credentials.ClientId}:{secretHash}";

        if (cache.TryGetValue(key, out string? cached) && cached is not null)
        {
            return cached;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{LoginBaseAddress}{Uri.EscapeDataString(credentials.TenantId)}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials"
            })
        };

        using var response = await GraphHttp.SendAsync(http, request, "Entra ID", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A mensagem do Entra ID diz o que está errado — secret expirado, client id
            // inexistente — e é exatamente o que o gestor precisa ler na tela.
            throw new EmailDeliveryException(
                $"O Entra ID recusou as credenciais ({(int)response.StatusCode}): {await GraphHttp.ErrorOf(response, cancellationToken)}");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var token = body.TryGetProperty("access_token", out var accessToken) ? accessToken.GetString() : null;

        if (string.IsNullOrEmpty(token))
        {
            throw new EmailDeliveryException("O Entra ID respondeu sem token de acesso.");
        }

        var expiresIn = body.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;

        // Um minuto de folga: token que expira no meio do envio vira falha à toa.
        cache.Set(key, token, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));

        return token;
    }
}

/// <summary>Chamada HTTP ao Graph ou ao Entra ID, com falhas traduzidas para o gestor.</summary>
internal static class GraphHttp
{
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpRequestMessage request, string service, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new EmailDeliveryException($"Não foi possível falar com o {service}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmailDeliveryException($"O {service} não respondeu a tempo.", ex);
        }
    }

    /// <summary>A descrição do erro que Graph e Entra ID mandam no corpo, curta.</summary>
    public static async Task<string> ErrorOf(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;

            if (root.TryGetProperty("error_description", out var description))
            {
                return Short(description.GetString());
            }

            if (root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message))
            {
                return Short(message.GetString());
            }
        }
        catch (JsonException)
        {
            // Corpo que não é JSON: devolve o começo dele.
        }

        return Short(text);
    }

    private static string Short(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "sem detalhe" : text.Length <= 500 ? text : text[..500];
}
