using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;

namespace OpsDesk.Application.Auth;

/// <summary>Aplicativo do Entra ID usado no login: o mesmo do e-mail, com o secret já decifrado.</summary>
public record EntraAppCredentials(string TenantId, string ClientId, string ClientSecret);

/// <summary>
/// Quem o Entra ID disse que entrou, depois de conferidos emissor, audiência, nonce e validade.
/// </summary>
/// <param name="ObjectId">Claim <c>oid</c>: estável enquanto a conta existir no tenant.</param>
public record EntraIdentity(string ObjectId, string Email, string? Name);

/// <summary>Resgate do código de autorização: a identidade, ou por que ela foi recusada.</summary>
public abstract record EntraRedeemResult
{
    public sealed record Succeeded(EntraIdentity Identity) : EntraRedeemResult;

    /// <param name="Reason">Para o log. Nunca vai para a tela: pode descrever o token.</param>
    public sealed record Failed(string Reason) : EntraRedeemResult;
}

/// <summary>
/// Conversa com o Entra ID no fluxo de código de autorização com PKCE (OpenID Connect).
///
/// O fluxo é conduzido pela API, não pelo navegador: o client secret já mora no servidor,
/// o código é trocado lá, e a sessão nasce no mesmo cookie de refresh do login com senha.
/// O SPA não ganha biblioteca de autenticação nem token do Entra ID na memória.
/// </summary>
public interface IEntraSignIn
{
    Uri BuildAuthorizeUrl(
        EntraAppCredentials app, string redirectUri, string state, string nonce, string codeChallenge);

    Task<EntraRedeemResult> RedeemCodeAsync(
        EntraAppCredentials app,
        string code,
        string redirectUri,
        string codeVerifier,
        string expectedNonce,
        CancellationToken cancellationToken);
}

/// <summary>
/// Se o login com a conta Microsoft está disponível, e com que aplicativo.
/// </summary>
public class MicrosoftSignInSettings(IOpsDeskDbContext db, ISecretProtector protector)
{
    /// <summary>
    /// Credenciais prontas, ou nulo quando o botão não deve aparecer: desligado na
    /// configuração geral, ou o aplicativo do Entra ID incompleto ou com secret ilegível.
    /// </summary>
    public async Task<EntraAppCredentials?> GetAsync(CancellationToken cancellationToken = default)
    {
        var enabled = await db.SystemSettings
            .AsNoTracking()
            .AnyAsync(s => s.MicrosoftSignInEnabled, cancellationToken);

        return enabled ? await GetAppAsync(cancellationToken) : null;
    }

    /// <summary>O aplicativo do Entra ID está configurado e utilizável, ligado ou não o login.</summary>
    public async Task<bool> IsAppReadyAsync(CancellationToken cancellationToken = default) =>
        await GetAppAsync(cancellationToken) is not null;

    private async Task<EntraAppCredentials?> GetAppAsync(CancellationToken cancellationToken)
    {
        var email = await db.EmailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        if (email is not { TenantId: { } tenant, ClientId: { } client, ProtectedClientSecret: { } stored }
            || !IsSingleTenant(tenant))
        {
            return null;
        }

        return protector.TryUnprotect(stored) is { } secret
            ? new EntraAppCredentials(tenant, client, secret)
            : null;
    }

    /// <summary>
    /// O login só é oferecido com um tenant específico. Os endereços genéricos do Entra ID
    /// (<c>common</c>, <c>organizations</c>, <c>consumers</c>) aceitam conta de qualquer
    /// empresa — ou conta pessoal —, e o sistema criaria solicitante para qualquer uma delas.
    /// </summary>
    public static bool IsSingleTenant(string tenant) =>
        !string.IsNullOrWhiteSpace(tenant)
        && tenant.Trim().ToLowerInvariant() is not ("common" or "organizations" or "consumers");
}
