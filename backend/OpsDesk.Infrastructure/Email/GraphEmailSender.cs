using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using OpsDesk.Application.Abstractions;

namespace OpsDesk.Infrastructure.Email;

/// <summary>
/// Envio pelo Microsoft Graph (<c>POST /users/{remetente}/sendMail</c>), com token de
/// aplicativo do Entra ID por client credentials.
///
/// HTTP direto, sem o SDK do Graph: são duas chamadas, e o SDK traria centenas de tipos
/// para usar dois.
///
/// O aplicativo precisa da permissão <c>Mail.Send</c> de aplicativo, restrita à caixa de
/// suporte por política de acesso do Exchange. Sem a restrição, a permissão vale para toda
/// caixa do tenant — ver README, seção 18.
/// </summary>
public class GraphEmailSender(HttpClient http, IMemoryCache cache) : IEmailSender
{
    public const string LoginBaseAddress = "https://login.microsoftonline.com/";
    public const string GraphBaseAddress = "https://graph.microsoft.com/v1.0/";

    public async Task SendAsync(
        EmailSenderCredentials credentials, EmailMessage message, CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(credentials, cancellationToken);

        var payload = new
        {
            message = new
            {
                subject = message.Subject,
                body = new { contentType = "HTML", content = message.HtmlBody },
                toRecipients = new[]
                {
                    new { emailAddress = new { address = message.ToAddress, name = message.ToName } }
                }
            },
            // Guardar na pasta de enviados da caixa de suporte deixa a trilha visível para
            // quem administra a caixa, sem depender só do banco do OpsDesk.
            saveToSentItems = true
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{GraphBaseAddress}users/{Uri.EscapeDataString(credentials.SenderAddress)}/sendMail")
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Authorization = new("Bearer", token);

        using var response = await SendOrThrowAsync(request, "Microsoft Graph", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new EmailDeliveryException(
                $"O Microsoft Graph recusou o envio ({(int)response.StatusCode}): {await ErrorOf(response, cancellationToken)}");
        }
    }

    private async Task<string> TokenAsync(EmailSenderCredentials credentials, CancellationToken cancellationToken)
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

        using var response = await SendOrThrowAsync(request, "Entra ID", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A mensagem do Entra ID diz o que está errado — secret expirado, client id
            // inexistente — e é exatamente o que o gestor precisa ler na tela.
            throw new EmailDeliveryException(
                $"O Entra ID recusou as credenciais ({(int)response.StatusCode}): {await ErrorOf(response, cancellationToken)}");
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

    private async Task<HttpResponseMessage> SendOrThrowAsync(
        HttpRequestMessage request, string service, CancellationToken cancellationToken)
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
    private static async Task<string> ErrorOf(HttpResponseMessage response, CancellationToken cancellationToken)
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
