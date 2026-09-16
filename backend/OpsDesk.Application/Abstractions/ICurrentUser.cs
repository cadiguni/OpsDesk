using OpsDesk.Application.Authorization;

namespace OpsDesk.Application.Abstractions;

/// <summary>Usuário autenticado na requisição atual, lido das claims do JWT.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    /// <summary>
    /// Identidade usada pelo filtro de visibilidade. Lança quando não há usuário
    /// autenticado: é erro de programação chamar isso em endpoint anônimo.
    /// </summary>
    TicketViewer Viewer { get; }
}
