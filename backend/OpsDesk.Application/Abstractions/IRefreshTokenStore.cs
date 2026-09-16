namespace OpsDesk.Application.Abstractions;

/// <summary>
/// Emissão, rotação e revogação de refresh token (decisão 4.6 de docs/arquitetura.md).
/// Quem guarda e compara hash é a implementação; a aplicação só lida com o valor em claro,
/// que vai para o cookie.
/// </summary>
public interface IRefreshTokenStore
{
    Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Troca o token apresentado por um novo. O antigo é revogado na mesma operação: um
    /// refresh token vale exatamente um uso.
    /// </summary>
    Task<RefreshTokenRotation> RotateAsync(string presentedToken, CancellationToken cancellationToken = default);

    /// <summary>Revoga o token apresentado. Idempotente: token desconhecido não é erro.</summary>
    Task RevokeAsync(string presentedToken, CancellationToken cancellationToken = default);

    /// <summary>Revoga todas as sessões ativas do usuário.</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

public readonly record struct IssuedRefreshToken(string Value, DateTimeOffset ExpiresAt);

public abstract record RefreshTokenRotation
{
    public sealed record Rotated(Guid UserId, IssuedRefreshToken Token) : RefreshTokenRotation;

    /// <summary>Token desconhecido, expirado ou já usado.</summary>
    public sealed record Rejected : RefreshTokenRotation;
}
