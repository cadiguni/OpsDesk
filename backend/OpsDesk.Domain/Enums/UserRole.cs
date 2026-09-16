namespace OpsDesk.Domain.Enums;

/// <summary>Perfil de acesso do usuário. Persistido como string.</summary>
public enum UserRole
{
    /// <summary>Solicitante. Vê apenas os próprios chamados, nunca comentário interno.</summary>
    Requester,

    /// <summary>Técnico. Vê chamados sem responsável e os atribuídos a ele.</summary>
    Technician,

    /// <summary>Gestor. Vê todos os chamados e o dashboard.</summary>
    Manager
}
