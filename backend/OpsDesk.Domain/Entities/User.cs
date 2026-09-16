using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>Usuário do sistema: solicitante, técnico ou gestor.</summary>
public class User : IHasUpdatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = null!;

    public string Email { get; set; } = null!;

    /// <summary>
    /// Anulável de propósito. A partir da versão 2.0, solicitantes identificados apenas pelo
    /// endereço de e-mail são criados sem senha e não conseguem autenticar até definirem uma.
    /// </summary>
    public string? PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.Requester;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Ticket> RequestedTickets { get; set; } = [];

    public ICollection<Ticket> AssignedTickets { get; set; } = [];

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    /// <summary>Técnico e gestor formam a equipe: veem comentário interno e o painel técnico.</summary>
    public bool IsStaff => Role is UserRole.Technician or UserRole.Manager;
}
