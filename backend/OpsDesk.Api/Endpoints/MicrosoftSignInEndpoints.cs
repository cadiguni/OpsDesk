using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Api.Authentication;
using OpsDesk.Api.RateLimiting;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Auth;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// Login com a conta Microsoft (Entra ID), por código de autorização com PKCE.
///
/// O navegador vai a <c>/start</c>, que o manda ao Entra ID; o Entra ID devolve a
/// <c>/callback</c>, que troca o código, abre a sessão no mesmo cookie de refresh do login
/// com senha e devolve o navegador ao SPA. O SPA, ao carregar, renova a sessão como em
/// qualquer recarga de página — não há caminho de sessão novo para ele conhecer.
///
/// As duas rotas ficam sob <c>/api/auth</c>, fora da trava de senha provisória, e
/// respondem sempre com redirecionamento: quem chega aqui é uma navegação do navegador, não
/// uma chamada do SPA, e erro em JSON seria uma página em branco para a pessoa.
/// </summary>
public static class MicrosoftSignInEndpoints
{
    public const string CallbackPath = "/api/auth/microsoft/callback";

    /// <summary>Cookie com o estado do fluxo em andamento: state, nonce, verificador PKCE e destino.</summary>
    public const string FlowCookieName = "opsdesk_entra_flow";

    private const string FlowCookiePath = "/api/auth/microsoft";

    private static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);

    private const string ProtectorPurpose = "OpsDesk.MicrosoftSignIn.Flow.v1";

    private sealed record Flow(string State, string Nonce, string CodeVerifier, string RedirectUri, string ReturnTo);

    public static IEndpointRouteBuilder MapMicrosoftSignInEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/auth/microsoft")
            .WithTags("Autenticação")
            .AllowAnonymous()
            .ExcludeFromDescription();

        group.MapGet("/start", Start)
            .RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithSummary("Leva o navegador ao login da Microsoft.");

        group.MapGet("/callback", Callback)
            .RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithSummary("Retorno do Entra ID: abre a sessão e devolve o navegador ao portal.");

        return routes;
    }

    private static async Task<IResult> Start(
        string? returnTo,
        HttpContext context,
        MicrosoftSignInSettings settings,
        IEntraSignIn entra,
        IDataProtectionProvider dataProtection,
        IOpsDeskDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var destination = await SafeReturnToAsync(returnTo, context.Request, db, configuration, cancellationToken);

        if (await settings.GetAsync(cancellationToken) is not { } app)
        {
            return Failure(destination, "indisponivel");
        }

        var flow = new Flow(
            State: Random(),
            Nonce: Random(),
            CodeVerifier: Random(),
            RedirectUri: $"{Origin(context.Request)}{context.Request.PathBase}{CallbackPath}",
            ReturnTo: destination);

        var payload = Protector(dataProtection).Protect(JsonSerializer.Serialize(flow), FlowLifetime);

        context.Response.Cookies.Append(FlowCookieName, payload, FlowCookieOptions(DateTimeOffset.UtcNow + FlowLifetime));

        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(flow.CodeVerifier)));

        return TypedResults.Redirect(
            entra.BuildAuthorizeUrl(app, flow.RedirectUri, flow.State, flow.Nonce, challenge).ToString());
    }

    private static async Task<IResult> Callback(
        string? code,
        string? state,
        string? error,
        HttpContext context,
        MicrosoftSignInSettings settings,
        IEntraSignIn entra,
        AuthService auth,
        IDataProtectionProvider dataProtection,
        IOpsDeskDbContext db,
        IConfiguration configuration,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var logger = loggers.CreateLogger(typeof(MicrosoftSignInEndpoints));

        // O fluxo serve uma vez: sai do navegador seja qual for o desfecho.
        var flow = ReadFlow(context.Request, dataProtection);
        context.Response.Cookies.Delete(FlowCookieName, FlowCookieOptions(null));

        var fallback = flow?.ReturnTo
                       ?? await SafeReturnToAsync(null, context.Request, db, configuration, cancellationToken);

        // Sem o cookie, ou com outro state, este retorno não é o do fluxo que este navegador
        // abriu: pode ser um link forjado para logar a vítima na conta de quem o montou.
        if (flow is null
            || state is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(flow.State)))
        {
            return Failure(fallback, "expirou");
        }

        if (error is not null || string.IsNullOrEmpty(code))
        {
            // access_denied é a pessoa desistindo ou o consentimento negado; o resto é
            // configuração, e é para o log.
            if (error != "access_denied")
            {
                logger.LogWarning("O Entra ID devolveu o login com erro: {Error}.", error ?? "sem código");
            }

            return Failure(flow.ReturnTo, error == "access_denied" ? "cancelado" : "falhou");
        }

        if (await settings.GetAsync(cancellationToken) is not { } app)
        {
            return Failure(flow.ReturnTo, "indisponivel");
        }

        var redeemed = await entra.RedeemCodeAsync(
            app, code, flow.RedirectUri, flow.CodeVerifier, flow.Nonce, cancellationToken);

        if (redeemed is not EntraRedeemResult.Succeeded { Identity: var identity })
        {
            logger.LogWarning(
                "Login com a conta Microsoft recusado: {Reason}", ((EntraRedeemResult.Failed)redeemed).Reason);

            return Failure(flow.ReturnTo, "falhou");
        }

        var result = await auth.SignInWithMicrosoftAsync(identity, cancellationToken);

        switch (result)
        {
            case AuthResult.Success success:
                RefreshTokenCookie.Write(context.Response, success.RefreshToken, success.RefreshTokenExpiresAt);

                return TypedResults.Redirect(flow.ReturnTo);

            case AuthResult.MicrosoftAccountRejected rejected:
                logger.LogInformation(
                    "Login com a conta Microsoft recusado para {ObjectId}: {Reason}.", identity.ObjectId, rejected.Reason);

                return Failure(flow.ReturnTo, rejected.Reason);

            default:
                throw new InvalidOperationException($"Resultado de login não tratado: {result.GetType().Name}.");
        }
    }

    /// <summary>
    /// Para onde devolver o navegador. Só endereço de origem conhecida — a do próprio pedido,
    /// a do portal na configuração de e-mail, ou uma das origens do CORS —, para o login não
    /// virar redirecionamento aberto: um link "entre com a Microsoft" que, depois do login de
    /// verdade, deixa a pessoa num site falso parecido com o portal.
    /// </summary>
    private static async Task<string> SafeReturnToAsync(
        string? returnTo,
        HttpRequest request,
        IOpsDeskDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var portalUrl = await db.EmailSettings
            .AsNoTracking()
            .Select(s => s.PortalUrl)
            .SingleOrDefaultAsync(cancellationToken);

        var portalOrigin = Uri.TryCreate(portalUrl, UriKind.Absolute, out var portal) ? OriginOf(portal) : null;

        var allowed = (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .Select(o => o.TrimEnd('/'))
            .Append(Origin(request))
            .Append(portalOrigin)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (Uri.TryCreate(returnTo, UriKind.Absolute, out var target)
            && target.Scheme is "https" or "http"
            && allowed.Contains(OriginOf(target)))
        {
            return target.GetLeftPart(UriPartial.Query);
        }

        return $"{portalOrigin ?? Origin(request)}/";
    }

    /// <summary>
    /// Volta ao login do SPA (`/entrar`) com o motivo, que a tela traduz. Só um código curto vai na URL:
    /// detalhe do Entra ID pode descrever configuração e fica no log.
    /// </summary>
    private static IResult Failure(string returnTo, string reason)
    {
        var origin = OriginOf(new Uri(returnTo));

        return TypedResults.Redirect($"{origin}/entrar?microsoft={Uri.EscapeDataString(reason)}");
    }

    private static Flow? ReadFlow(HttpRequest request, IDataProtectionProvider dataProtection)
    {
        if (request.Cookies[FlowCookieName] is not { Length: > 0 } payload)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Flow>(Protector(dataProtection).Unprotect(payload));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // Expirado, adulterado, ou cifrado com chave que não existe mais.
            return null;
        }
    }

    private static ITimeLimitedDataProtector Protector(IDataProtectionProvider provider) =>
        provider.CreateProtector(ProtectorPurpose).ToTimeLimitedDataProtector();

    private static CookieOptions FlowCookieOptions(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,

        // Lax, e não Strict como o cookie de refresh: o retorno do Entra ID é uma navegação
        // vinda de outro site, e cookie Strict não acompanha navegação entre sites. Lax
        // acompanha só GET de navegação de topo — exatamente o retorno com response_mode=query.
        SameSite = SameSiteMode.Lax,
        Path = FlowCookiePath,
        Expires = expiresAt,
        IsEssential = true
    };

    private static string Origin(HttpRequest request) => $"{request.Scheme}://{request.Host}";

    private static string OriginOf(Uri uri) => uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');

    private static string Random() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
