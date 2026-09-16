using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Auth;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Authentication;

/// <summary>
/// Emite o JWT de acesso.
///
/// As claims carregam id, nome, e-mail e perfil. O perfil vai no token para que a
/// autorização de rota não precise de uma consulta ao banco por requisição — mas atenção:
/// o filtro de visibilidade continua sendo o que decide *quais* chamados a pessoa vê, e
/// ele deriva desta mesma claim. Token curto é o que limita a janela em que um perfil
/// rebaixado no banco continuaria valendo.
/// </summary>
public class JwtAccessTokenService(IOptions<JwtOptions> options, IClock clock) : IAccessTokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Create(User user)
    {
        var issuedAt = clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [OpsDeskClaims.Subject] = user.Id.ToString(),
                [OpsDeskClaims.Name] = user.Name,
                [OpsDeskClaims.Email] = user.Email,
                [OpsDeskClaims.Role] = user.Role.ToString()
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
