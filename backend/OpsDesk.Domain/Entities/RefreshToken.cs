using OpsDesk.Domain.Common;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Refresh token revogável. Guardamos apenas o hash: o valor em claro vive no cookie
/// <c>httpOnly</c> do navegador e é rotacionado a cada uso.
/// </summary>
public class RefreshToken : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Hash do token que substituiu este na rotação. Deixa a cadeia auditável.</summary>
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
