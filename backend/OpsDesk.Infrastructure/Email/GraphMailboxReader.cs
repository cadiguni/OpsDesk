using System.Net.Http.Json;
using System.Text.Json;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Inbound;

namespace OpsDesk.Infrastructure.Email;

/// <summary>
/// Leitura da caixa monitorada pelo Microsoft Graph: mensagens não lidas da caixa de
/// entrada, das mais antigas para as mais novas, com o corpo pedido em texto puro.
///
/// Texto puro de propósito (<c>Prefer: outlook.body-content-type="text"</c>): o HTML de um
/// e-mail desconhecido não entra no sistema, e assim não há HTML para sanitizar na caixa
/// de SPAM, que é justamente onde chega o conteúdo mal-intencionado.
///
/// Exige a permissão <c>Mail.ReadWrite</c> de aplicativo — ler e marcar como lida —,
/// restrita às caixas de suporte por política de acesso do Exchange, como o <c>Mail.Send</c>.
/// </summary>
public class GraphMailboxReader(HttpClient http, GraphTokenProvider tokens) : IMailboxReader
{
    public async Task<IReadOnlyList<FetchedMessage>> FetchUnreadAsync(
        EmailSenderCredentials credentials, string mailbox, int max, CancellationToken cancellationToken = default)
    {
        var token = await tokens.GetAsync(credentials, cancellationToken);

        var url = $"{GraphEmailSender.GraphBaseAddress}users/{Uri.EscapeDataString(mailbox)}/mailFolders/inbox/messages" +
                  $"?$filter=isRead eq false&$orderby=receivedDateTime asc&$top={max}" +
                  "&$select=id,internetMessageId,subject,from,toRecipients,receivedDateTime,body,internetMessageHeaders";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");

        using var response = await GraphHttp.SendAsync(http, request, "Microsoft Graph", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new EmailDeliveryException(
                $"O Microsoft Graph recusou a leitura de {mailbox} ({(int)response.StatusCode}): {await GraphHttp.ErrorOf(response, cancellationToken)}");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        return [.. body.GetProperty("value").EnumerateArray().Select(ToMessage)];
    }

    public async Task MarkAsReadAsync(
        EmailSenderCredentials credentials, string mailbox, string providerId, CancellationToken cancellationToken = default)
    {
        var token = await tokens.GetAsync(credentials, cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{GraphEmailSender.GraphBaseAddress}users/{Uri.EscapeDataString(mailbox)}/messages/{Uri.EscapeDataString(providerId)}")
        {
            Content = JsonContent.Create(new { isRead = true })
        };

        request.Headers.Authorization = new("Bearer", token);

        using var response = await GraphHttp.SendAsync(http, request, "Microsoft Graph", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new EmailDeliveryException(
                $"O Microsoft Graph não marcou a mensagem como lida ({(int)response.StatusCode}): {await GraphHttp.ErrorOf(response, cancellationToken)}");
        }
    }

    private static FetchedMessage ToMessage(JsonElement message)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (message.TryGetProperty("internetMessageHeaders", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var header in list.EnumerateArray())
            {
                var name = header.GetProperty("name").GetString()?.ToLowerInvariant();
                var value = header.GetProperty("value").GetString();

                if (name is not null && value is not null)
                {
                    // Cabeçalho repetido (Received, por exemplo) fica com o primeiro: nenhuma
                    // regra daqui depende dos repetidos.
                    headers.TryAdd(name, value);
                }
            }
        }

        var from = message.TryGetProperty("from", out var fromElement) && fromElement.ValueKind == JsonValueKind.Object
            ? fromElement.GetProperty("emailAddress")
            : default;

        var to = message.TryGetProperty("toRecipients", out var recipients) && recipients.GetArrayLength() > 0
            ? recipients[0].GetProperty("emailAddress").GetProperty("address").GetString()
            : null;

        return new FetchedMessage(
            message.GetProperty("id").GetString()!,
            Text(message, "internetMessageId"),
            headers.GetValueOrDefault("in-reply-to"),
            headers.GetValueOrDefault("references"),
            from.ValueKind == JsonValueKind.Object ? Text(from, "name") ?? "" : "",
            from.ValueKind == JsonValueKind.Object ? Text(from, "address") ?? "" : "",
            to,
            Text(message, "subject") ?? "",
            message.TryGetProperty("body", out var bodyElement) ? Text(bodyElement, "content") ?? "" : "",
            message.TryGetProperty("receivedDateTime", out var received) ? received.GetDateTimeOffset() : DateTimeOffset.UtcNow,
            headers);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
