using OpsDesk.Domain.Common;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Link de "definir senha": a redefinição pedida por quem esqueceu, e o convite de quem
/// foi cadastrado pelo gestor.
///
/// Guarda só o hash do token, como o refresh token: quem lê o banco não consegue montar o
/// link. O token vale uma vez e por pouco tempo.
/// </summary>
public class PasswordResetToken : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>SHA-256 do token, em hexadecimal.</summary>
    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Usado, ou invalidado por um pedido mais novo.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
