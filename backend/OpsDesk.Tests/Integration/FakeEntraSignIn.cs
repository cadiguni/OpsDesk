using OpsDesk.Application.Auth;
using OpsDesk.Infrastructure.Authentication;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Entra ID falso: monta a URL de autorização como o de verdade, e no resgate do código
/// devolve a identidade que o teste escolheu. Nenhum teste fala com a Microsoft.
/// </summary>
public class FakeEntraSignIn : IEntraSignIn
{
    private readonly EntraSignIn _real = new(new HttpClient(), new FixedClock(DateTimeOffset.UtcNow), Microsoft.Extensions.Logging.Abstractions.NullLogger<EntraSignIn>.Instance);

    /// <summary>Quem o próximo resgate diz que entrou. Nulo faz o resgate falhar.</summary>
    public EntraIdentity? NextIdentity { get; set; }

    public List<(string Code, string RedirectUri, string CodeVerifier, string Nonce)> Redeemed { get; } = [];

    public void Reset()
    {
        NextIdentity = null;
        Redeemed.Clear();
    }

    public Uri BuildAuthorizeUrl(EntraAppCredentials app, string redirectUri, string state, string nonce, string codeChallenge) =>
        _real.BuildAuthorizeUrl(app, redirectUri, state, nonce, codeChallenge);

    public Task<EntraRedeemResult> RedeemCodeAsync(
        EntraAppCredentials app, string code, string redirectUri, string codeVerifier, string expectedNonce, CancellationToken cancellationToken)
    {
        Redeemed.Add((code, redirectUri, codeVerifier, expectedNonce));

        return Task.FromResult<EntraRedeemResult>(NextIdentity is { } identity
            ? new EntraRedeemResult.Succeeded(identity)
            : new EntraRedeemResult.Failed("recusado pelo teste"));
    }
}
