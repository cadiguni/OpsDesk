namespace OpsDesk.Api.Authentication;

/// <summary>
/// Cookie que carrega o refresh token (decisão 4.6 de docs/arquitetura.md).
///
/// O token de acesso vai no corpo da resposta e vive em memória no navegador; o refresh
/// token vive só aqui, como <c>httpOnly</c>, fora do alcance de qualquer JavaScript e
/// portanto de qualquer XSS.
/// </summary>
internal static class RefreshTokenCookie
{
    public const string Name = "opsdesk_refresh";

    /// <summary>
    /// O cookie só é enviado para os endpoints de autenticação. Restringir o caminho
    /// significa que ele não acompanha nenhuma das centenas de chamadas a
    /// <c>/api/tickets</c> — menos superfície, e nada de token em log de proxy.
    /// </summary>
    private const string RestrictedPath = "/api/auth";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, token, Options(expiresAt));

    public static void Delete(HttpResponse response) =>
        // A remoção tem que repetir Path, Secure e SameSite: o navegador só apaga o
        // cookie quando esses atributos batem com os do cookie que ele guardou.
        response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,

        // Mantido ligado também em desenvolvimento. Chrome e Firefox tratam localhost
        // como origem confiável e aceitam cookie Secure sobre HTTP ali, então não há o
        // clássico "funciona em dev e quebra em produção" para descobrir depois.
        Secure = true,

        // Same-site é avaliado por domínio registrável, não por porta: localhost:5173 e
        // localhost:8080 são o mesmo site, e opsdesk.empresa.com e api.empresa.com também.
        // Strict não atrapalha esses casos e barra envio a partir de sites de terceiros.
        SameSite = SameSiteMode.Strict,

        Path = RestrictedPath,
        Expires = expiresAt,
        IsEssential = true
    };
}
