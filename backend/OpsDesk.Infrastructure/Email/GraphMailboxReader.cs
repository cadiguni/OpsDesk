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

    /// <summary>
    /// Duas etapas: a lista só com os metadados, e depois cada anexo que cabe, um por vez.
    /// Pedir a lista completa traria o conteúdo de todos de uma vez, inclusive o vídeo de
    /// cem megabytes que seria descartado pelo tamanho.
    ///
    /// Só <c>fileAttachment</c>: mensagem anexada (<c>itemAttachment</c>) e link para o
    /// OneDrive (<c>referenceAttachment</c>) não têm arquivo para guardar.
    /// </summary>
    public async Task<IReadOnlyList<FetchedAttachment>> FetchAttachmentsAsync(
        EmailSenderCredentials credentials,
        string mailbox,
        string providerId,
        long maxSizeInBytes,
        CancellationToken cancellationToken = default)
    {
        var token = await tokens.GetAsync(credentials, cancellationToken);

        var baseUrl = $"{GraphEmailSender.GraphBaseAddress}users/{Uri.EscapeDataString(mailbox)}" +
                      $"/messages/{Uri.EscapeDataString(providerId)}/attachments";

        var list = await GetAsync($"{baseUrl}?$select=id,name,contentType,size,isInline", token, cancellationToken);

        if (list is null)
        {
            return [];
        }

        var result = new List<FetchedAttachment>();

        foreach (var item in list.Value.GetProperty("value").EnumerateArray())
        {
            if (Text(item, "@odata.type") is { } type && type != FileAttachmentType)
            {
                continue;
            }

            if (item.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) && bytes > maxSizeInBytes)
            {
                continue;
            }

            if (Text(item, "id") is not { } id
                || await GetAsync($"{baseUrl}/{Uri.EscapeDataString(id)}", token, cancellationToken) is not { } full
                || Text(full, "contentBytes") is not { } base64)
            {
                continue;
            }

            var content = Convert.FromBase64String(base64);

            if (content.Length > maxSizeInBytes)
            {
                continue;
            }

            result.Add(new FetchedAttachment(
                Text(full, "name") ?? "anexo",
                Text(full, "contentType") ?? "application/octet-stream",
                content,
                full.TryGetProperty("isInline", out var inline) && inline.ValueKind == JsonValueKind.True,
                Text(full, "contentId")));
        }

        return result;
    }

    public async Task<string?> FindByMessageIdAsync(
        EmailSenderCredentials credentials, string mailbox, string messageId, CancellationToken cancellationToken = default)
    {
        var token = await tokens.GetAsync(credentials, cancellationToken);

        // Aspas simples dobradas, como pede o OData; o valor inteiro vai codificado, porque
        // Message-ID carrega '+', '=' e '/' que a query string leria de outro jeito.
        var filter = $"internetMessageId eq '{messageId.Replace("'", "''")}'";
        var url = $"{GraphEmailSender.GraphBaseAddress}users/{Uri.EscapeDataString(mailbox)}/messages" +
                  $"?$filter={Uri.EscapeDataString(filter)}&$select=id&$top=1";

        var body = await GetAsync(url, token, cancellationToken);

        return body is { } found && found.GetProperty("value").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } first
            ? Text(first, "id")
            : null;
    }

    private const string FileAttachmentType = "#microsoft.graph.fileAttachment";

    /// <summary>O corpo da resposta, ou nulo quando a mensagem ou o anexo não existe mais.</summary>
    private async Task<JsonElement?> GetAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);

        using var response = await GraphHttp.SendAsync(http, request, "Microsoft Graph", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new EmailDeliveryException(
                $"O Microsoft Graph recusou a leitura dos anexos ({(int)response.StatusCode}): {await GraphHttp.ErrorOf(response, cancellationToken)}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
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
