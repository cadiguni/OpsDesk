using System.Net.Http.Json;
using OpsDesk.Application.Abstractions;

namespace OpsDesk.Infrastructure.Email;

/// <summary>
/// Envio pelo Microsoft Graph (<c>POST /users/{remetente}/sendMail</c>).
///
/// HTTP direto, sem o SDK do Graph: são poucas chamadas, e o SDK traria centenas de tipos
/// para usar duas.
///
/// O aplicativo precisa da permissão <c>Mail.Send</c> de aplicativo, restrita à caixa de
/// suporte por política de acesso do Exchange. Sem a restrição, a permissão vale para toda
/// caixa do tenant — ver README, seção 18.
/// </summary>
public class GraphEmailSender(HttpClient http, GraphTokenProvider tokens) : IEmailSender
{
    public const string GraphBaseAddress = "https://graph.microsoft.com/v1.0/";

    public async Task SendAsync(
        EmailSenderCredentials credentials, EmailMessage message, CancellationToken cancellationToken = default)
    {
        var token = await tokens.GetAsync(credentials, cancellationToken);

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

        using var response = await GraphHttp.SendAsync(http, request, "Microsoft Graph", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new EmailDeliveryException(
                $"O Microsoft Graph recusou o envio ({(int)response.StatusCode}): {await GraphHttp.ErrorOf(response, cancellationToken)}");
        }
    }
}
