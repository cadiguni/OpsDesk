using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Auth;

namespace OpsDesk.Infrastructure.Authentication;

/// <summary>
/// Fluxo de código de autorização com PKCE contra o endpoint v2.0 do Entra ID.
/// </summary>
public class EntraSignIn(HttpClient http, IClock clock, ILogger<EntraSignIn> logger) : IEntraSignIn
{
    public const string LoginBaseAddress = "https://login.microsoftonline.com/";

    /// <summary>
    /// Só o necessário para saber quem é a pessoa. Nada de <c>User.Read</c> nem de acesso ao
    /// Graph em nome dela: o OpsDesk não lê nada da conta além do que vem no id token.
    /// </summary>
    private const string Scope = "openid profile email";

    public Uri BuildAuthorizeUrl(
        EntraAppCredentials app, string redirectUri, string state, string nonce, string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = app.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,

            // Query, e não form_post: o retorno é um GET de navegação, e é nele que o
            // navegador manda o cookie SameSite=Lax com o estado do fluxo. Um POST vindo de
            // login.microsoftonline.com chegaria sem cookie nenhum.
            ["response_mode"] = "query",
            ["scope"] = Scope,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",

            // Em computador compartilhado, entrar direto com a conta que ficou no navegador
            // abriria o OpsDesk de outra pessoa.
            ["prompt"] = "select_account"
        };

        var encoded = string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        return new Uri($"{LoginBaseAddress}{Uri.EscapeDataString(app.TenantId)}/oauth2/v2.0/authorize?{encoded}");
    }

    public async Task<EntraRedeemResult> RedeemCodeAsync(
        EntraAppCredentials app,
        string code,
        string redirectUri,
        string codeVerifier,
        string expectedNonce,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{LoginBaseAddress}{Uri.EscapeDataString(app.TenantId)}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = app.ClientId,
                ["client_secret"] = app.ClientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier,
                ["scope"] = Scope
            })
        };

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Não foi possível falar com o Entra ID para concluir o login.");

            return new EntraRedeemResult.Failed("Entra ID inacessível.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // A descrição do Entra ID diz o que corrigir — secret expirado, redirect URI
                // não registrado — e é para quem administra, no log.
                return new EntraRedeemResult.Failed($"Entra ID recusou o código ({(int)response.StatusCode}): {Short(body)}");
            }

            string? idToken;

            try
            {
                using var json = JsonDocument.Parse(body);
                idToken = json.RootElement.TryGetProperty("id_token", out var token) ? token.GetString() : null;
            }
            catch (JsonException)
            {
                idToken = null;
            }

            return idToken is null
                ? new EntraRedeemResult.Failed("Entra ID respondeu sem id token.")
                : EntraIdTokenReader.Read(idToken, app, expectedNonce, clock.UtcNow);
        }
    }

    private static string Short(string text) => text.Length <= 500 ? text : text[..500];
}

/// <summary>
/// Leitura e conferência do id token devolvido pelo endpoint de token.
///
/// A assinatura não é verificada, e isso é deliberado e previsto pelo OpenID Connect
/// (Core 1.0, seção 3.1.3.7, item 6): o token veio direto do endpoint de token do Entra ID,
/// por TLS, numa requisição autenticada pelo client secret — não passou pelo navegador, então
/// não há por onde ser forjado. O que continua obrigatório, e é conferido aqui, é emissor,
/// audiência, tenant, nonce e validade. Se um dia o id token passar a chegar pelo navegador
/// (fluxo implícito, form_post com id_token), esta dispensa deixa de valer.
/// </summary>
public static class EntraIdTokenReader
{
    /// <summary>Tolerância de relógio entre este servidor e o Entra ID.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public static EntraRedeemResult Read(
        string idToken, EntraAppCredentials app, string expectedNonce, DateTimeOffset now)
    {
        var parts = idToken.Split('.');

        if (parts.Length != 3)
        {
            return Fail("id token malformado");
        }

        JsonElement claims;

        try
        {
            using var json = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            claims = json.RootElement.Clone();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return Fail("id token ilegível");
        }

        var tenantId = String(claims, "tid");

        if (tenantId is null)
        {
            return Fail("id token sem tid");
        }

        // Com o tenant configurado como GUID, a conferência é direta. Configurado pelo
        // domínio (empresa.onmicrosoft.com), o endpoint /{tenant}/ já só emite token para
        // contas daquele tenant, e o emissor abaixo amarra o tid ao token.
        if (Guid.TryParse(app.TenantId, out var configured)
            && !(Guid.TryParse(tenantId, out var actual) && actual == configured))
        {
            return Fail("tenant diferente do configurado");
        }

        if (String(claims, "iss") != $"https://login.microsoftonline.com/{tenantId}/v2.0")
        {
            return Fail("emissor inesperado");
        }

        if (!HasAudience(claims, app.ClientId))
        {
            return Fail("audiência diferente do client id");
        }

        // O nonce amarra o token a este fluxo, aberto por este navegador.
        if (String(claims, "nonce") is not { } nonce
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(nonce), Encoding.UTF8.GetBytes(expectedNonce)))
        {
            return Fail("nonce não confere");
        }

        if (Number(claims, "exp") is not { } exp || DateTimeOffset.FromUnixTimeSeconds(exp) + ClockSkew < now)
        {
            return Fail("id token expirado");
        }

        if (Number(claims, "nbf") is { } nbf && DateTimeOffset.FromUnixTimeSeconds(nbf) - ClockSkew > now)
        {
            return Fail("id token ainda não vale");
        }

        if (String(claims, "oid") is not { Length: > 0 } objectId)
        {
            return Fail("id token sem oid");
        }

        // `email` só vem quando a conta tem endereço de e-mail; `preferred_username` é o UPN,
        // que nas empresas costuma ser o próprio e-mail. Num tenant único os dois são
        // definidos pelo administrador, e não pela pessoa.
        var email = String(claims, "email") is { } e && e.Contains('@') ? e
            : String(claims, "preferred_username") is { } upn && upn.Contains('@') ? upn
            : null;

        if (email is null)
        {
            return Fail("id token sem e-mail");
        }

        return new EntraRedeemResult.Succeeded(new EntraIdentity(objectId, email.Trim(), String(claims, "name")));
    }

    private static EntraRedeemResult Fail(string reason) => new EntraRedeemResult.Failed($"id token recusado: {reason}.");

    private static bool HasAudience(JsonElement claims, string clientId)
    {
        if (!claims.TryGetProperty("aud", out var aud))
        {
            return false;
        }

        return aud.ValueKind switch
        {
            JsonValueKind.String => string.Equals(aud.GetString(), clientId, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => aud.GetArrayLength() == 1
                                   && string.Equals(aud[0].GetString(), clientId, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string? String(JsonElement claims, string name) =>
        claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement claims, string name) =>
        claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)
            ? n
            : null;

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new FormatException() };

        return Convert.FromBase64String(padded);
    }
}
