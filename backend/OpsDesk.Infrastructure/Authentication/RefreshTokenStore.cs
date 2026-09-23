using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Entities;
using OpsDesk.Infrastructure.Persistence;

namespace OpsDesk.Infrastructure.Authentication;

/// <summary>
/// Refresh token com rotação e detecção de reuso.
///
/// O token é 32 bytes aleatórios de fonte criptográfica. No banco guardamos apenas o
/// SHA-256 dele: um dump do banco não permite assumir sessão de ninguém.
///
/// Sobre o hash ser SHA-256 e não <c>PasswordHasher</c>: a invariante 8 do CLAUDE.md fala
/// de senha, que é segredo de baixa entropia e precisa de KDF lento para resistir a
/// dicionário. Este token é 256 bits aleatórios — não existe dicionário que o alcance, e
/// pagar milhares de iterações de PBKDF2 a cada renovação de sessão só adicionaria
/// latência sem comprar segurança.
/// </summary>
public class RefreshTokenStore(
    OpsDeskDbContext db,
    IOptions<JwtOptions> options,
    IClock clock,
    ILogger<RefreshTokenStore> logger) : IRefreshTokenStore
{
    private const int TokenBytes = 32;

    private readonly JwtOptions _options = options.Value;

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var issued = Create(userId);

        db.RefreshTokens.Add(issued.Entity);
        await db.SaveChangesAsync(cancellationToken);

        return issued.Token;
    }

    public async Task<RefreshTokenRotation> RotateAsync(
        string presentedToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return new RefreshTokenRotation.Rejected();
        }

        var hash = Hash(presentedToken);

        var existing = await db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            return new RefreshTokenRotation.Rejected();
        }

        var now = clock.UtcNow;

        if (existing.RevokedAt is { } revokedAt)
        {
            // Revogação deliberada não tem tolerância nenhuma.
            //
            // A distinção está em `ReplacedByTokenHash`: rotação preenche os dois campos,
            // revogação deliberada — logout, troca de senha, conta desativada, ou o corte
            // que segue uma suspeita de vazamento — preenche só `RevokedAt`.
            //
            // Sem esta separação a janela de tolerância anulava a revogação: quem trocasse
            // a senha para cortar um intruso veria o navegador dele renovar a sessão
            // segundos depois, com token novo em folha. O mesmo valia para o logout e,
            // pior, para o próprio corte por reuso detectado — o token do usuário legítimo
            // acabava de ser revogado, e a renovação seguinte o ressuscitava.
            if (existing.ReplacedByTokenHash is null)
            {
                return new RefreshTokenRotation.Rejected();
            }

            var sinceRotation = now - revokedAt;

            if (sinceRotation <= TimeSpan.FromSeconds(_options.RefreshTokenGraceSeconds))
            {
                // Dentro da janela de tolerância: duas renovações concorrentes do próprio
                // cliente. Emitimos um token novo em vez de tratar como vazamento.
                //
                // O vínculo de substituição do token apresentado não é reescrito: ele
                // registra a primeira rotação, que é o que realmente aconteceu primeiro.
                var concurrent = Create(existing.UserId);

                db.RefreshTokens.Add(concurrent.Entity);
                await db.SaveChangesAsync(cancellationToken);

                logger.LogDebug(
                    "Renovação concorrente dentro da tolerância para o usuário {UserId}.",
                    existing.UserId);

                return new RefreshTokenRotation.Rotated(existing.UserId, concurrent.Token);
            }

            // Fora da janela: o cliente legítimo já descartou este token há tempo. Ou o
            // cookie vazou, ou alguém está reproduzindo tráfego — nos dois casos derrubamos
            // todas as sessões, que é o único jeito de cortar o acesso de quem o roubou.
            logger.LogWarning(
                "Reuso de refresh token detectado para o usuário {UserId} " +
                "{Elapsed} após a rotação. Revogando todas as sessões.",
                existing.UserId,
                sinceRotation);

            await RevokeAllForUserAsync(existing.UserId, cancellationToken);

            return new RefreshTokenRotation.Rejected();
        }

        if (existing.ExpiresAt <= now)
        {
            return new RefreshTokenRotation.Rejected();
        }

        var replacement = Create(existing.UserId);

        existing.RevokedAt = now;
        existing.ReplacedByTokenHash = replacement.Entity.TokenHash;

        db.RefreshTokens.Add(replacement.Entity);
        await db.SaveChangesAsync(cancellationToken);

        return new RefreshTokenRotation.Rotated(existing.UserId, replacement.Token);
    }

    public async Task RevokeAsync(string presentedToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return;
        }

        var hash = Hash(presentedToken);

        // Logout de token desconhecido não é erro: o cliente pode estar com um cookie
        // antigo, e devolver falha só o deixaria preso a uma sessão que não existe.
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, clock.UtcNow), cancellationToken);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, clock.UtcNow), cancellationToken);
    }

    private (RefreshToken Entity, IssuedRefreshToken Token) Create(Guid userId)
    {
        var value = Base64Url.Encode(RandomNumberGenerator.GetBytes(TokenBytes));
        var expiresAt = clock.UtcNow.AddDays(_options.RefreshTokenDays);

        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(value),
            ExpiresAt = expiresAt
        };

        return (entity, new IssuedRefreshToken(value, expiresAt));
    }

    private static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}

/// <summary>
/// Base64 seguro para URL e cookie. O Base64 comum usa <c>+</c>, <c>/</c> e <c>=</c>,
/// que exigiriam escape no cabeçalho <c>Set-Cookie</c>.
/// </summary>
internal static class Base64Url
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
