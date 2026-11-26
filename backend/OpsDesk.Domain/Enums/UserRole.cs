namespace OpsDesk.Domain.Enums;

/// <summary>Perfil de acesso do usuário. Persistido como string.</summary>
public enum UserRole
{
    /// <summary>Solicitante. Vê apenas os próprios chamados, nunca comentário interno.</summary>
    Requester,

    /// <summary>Técnico. Vê chamados sem responsável e os atribuídos a ele.</summary>
    Technician,

    /// <summary>
    /// Administrador do portal: tudo o que o técnico faz, mais dashboard, usuários,
    /// catálogos e configurações.
    ///
    /// Na interface e na documentação se chama "Administrador"; até a versão 2.0 se chamava
    /// "Gestor". O valor gravado no banco e levado no token continua <c>Manager</c>: trocá-lo
    /// pediria migração de dados e derrubaria as sessões abertas, para um nome que ninguém vê.
    /// </summary>
    Manager
}
