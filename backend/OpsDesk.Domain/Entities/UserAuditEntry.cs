using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Trilha de auditoria de usuário: quem promoveu, desativou ou redefiniu a senha de quem,
/// e quando. Append-only, como o <c>TicketHistory</c>, e preenchida pelo mesmo tipo de
/// interceptor — nunca à mão nos serviços.
/// </summary>
public class UserAuditEntry : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public UserAuditAction Action { get; set; }

    public string? PreviousValue { get; set; }

    public string? NewValue { get; set; }

    /// <summary>Quem fez. Nulo quando não havia sessão — autocadastro, link de e-mail.</summary>
    public Guid? ChangedById { get; set; }

    public User? ChangedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
