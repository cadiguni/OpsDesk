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

    /// <summary>
    /// Obriga a trocar a senha antes de usar o sistema.
    ///
    /// Nasce do bootstrap do primeiro gestor, cuja senha chega por variável de ambiente —
    /// ou seja, por um canal que registra a credencial em configuração, em log de deploy e
    /// em dump de ambiente. Enquanto isto for verdadeiro, a API recusa toda rota que não
    /// seja a de autenticação, e a interface não deixa navegar para outro lugar.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Ticket> RequestedTickets { get; set; } = [];

    public ICollection<Ticket> AssignedTickets { get; set; } = [];

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    /// <summary>Técnico e gestor formam a equipe: veem comentário interno e o painel técnico.</summary>
    public bool IsStaff => Role is UserRole.Technician or UserRole.Manager;
}
