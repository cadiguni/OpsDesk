using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using OpsDesk.Application.Abstractions;
using OpsDesk.Infrastructure.Email;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// O formato das duas chamadas ao Microsoft 365 — token no Entra ID e <c>sendMail</c> no
/// Graph — e a tradução dos erros deles em mensagem legível para o gestor.
/// </summary>
public class GraphEmailSenderTests
{
    private static readonly EmailSenderCredentials Credentials =
        new("empresa.onmicrosoft.com", "11111111-2222-3333-4444-555555555555", "segredo-do-app", "suporte@empresa.com", "Suporte TI");

    private static readonly EmailMessage Message =
        new("fulano@empresa.com", "Fulano", "[OPS-000123] Nova resposta", "<p>Olá</p>");

    [Fact]
    public async Task Pede_token_por_client_credentials_e_envia_pela_caixa_de_suporte()
    {
        var handler = new RecordingHandler(TokenOk(), Accepted());

        await Sender(handler).SendAsync(Credentials, Message);

        var token = handler.Requests[0];
        Assert.Equal("https://login.microsoftonline.com/empresa.onmicrosoft.com/oauth2/v2.0/token", token.Uri);
        Assert.Contains("grant_type=client_credentials", token.Body);
        Assert.Contains("scope=https%3A%2F%2Fgraph.microsoft.com%2F.default", token.Body);

        var send = handler.Requests[1];
        Assert.Equal("https://graph.microsoft.com/v1.0/users/suporte%40empresa.com/sendMail", send.Uri);
        Assert.Equal("Bearer token-de-teste", send.Authorization);

        using var json = JsonDocument.Parse(send.Body);
        var message = json.RootElement.GetProperty("message");
        Assert.Equal("[OPS-000123] Nova resposta", message.GetProperty("subject").GetString());
        Assert.Equal("HTML", message.GetProperty("body").GetProperty("contentType").GetString());
        Assert.Equal("fulano@empresa.com",
            message.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString());
    }

    [Fact]
    public async Task Reaproveita_o_token_entre_envios()
    {
        var handler = new RecordingHandler(TokenOk(), Accepted(), Accepted());
        var sender = Sender(handler);

        await sender.SendAsync(Credentials, Message);
        await sender.SendAsync(Credentials, Message);

        Assert.Single(handler.Requests, r => r.Uri.Contains("/oauth2/"));
    }

    [Fact]
    public async Task Trocar_o_secret_pede_token_novo()
    {
        var handler = new RecordingHandler(TokenOk(), Accepted(), TokenOk(), Accepted());
        var sender = Sender(handler);

        await sender.SendAsync(Credentials, Message);
        await sender.SendAsync(Credentials with { ClientSecret = "segredo-novo" }, Message);

        Assert.Equal(2, handler.Requests.Count(r => r.Uri.Contains("/oauth2/")));
    }

    [Fact]
    public async Task Secret_recusado_vira_mensagem_do_entra_id_sem_o_secret()
    {
        var handler = new RecordingHandler(Json(HttpStatusCode.Unauthorized,
            """{"error":"invalid_client","error_description":"AADSTS7000222: The provided client secret keys are expired."}"""));

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler).SendAsync(Credentials, Message));

        Assert.Contains("expired", error.Message);
        Assert.DoesNotContain("segredo-do-app", error.Message);
    }

    [Fact]
    public async Task Recusa_do_graph_vira_mensagem_legivel()
    {
        var handler = new RecordingHandler(TokenOk(), Json(HttpStatusCode.Forbidden,
            """{"error":{"code":"ErrorAccessDenied","message":"Access to OData is disabled."}}"""));

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler).SendAsync(Credentials, Message));

        Assert.Contains("403", error.Message);
        Assert.Contains("Access to OData is disabled.", error.Message);
    }

    private static GraphEmailSender Sender(RecordingHandler handler) =>
        new(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()));

    private static HttpResponseMessage TokenOk() =>
        Json(HttpStatusCode.OK, """{"access_token":"token-de-teste","expires_in":3599,"token_type":"Bearer"}""");

    private static HttpResponseMessage Accepted() => new(HttpStatusCode.Accepted);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private record RecordedRequest(string Uri, string Body, string? Authorization);

    private class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsoluteUri,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString()));

            return _responses.Dequeue();
        }
    }
}
