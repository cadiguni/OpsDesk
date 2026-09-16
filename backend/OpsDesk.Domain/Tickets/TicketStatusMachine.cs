using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Tickets;

/// <summary>
/// Transições permitidas do chamado. Fonte de verdade única: nenhum serviço decide
/// por conta própria se uma mudança de status é válida.
/// O grafo está documentado no README, seção 5.8.
/// </summary>
public static class TicketStatusMachine
{
    private static readonly Dictionary<TicketStatus, TicketStatus[]> Allowed = new()
    {
        [TicketStatus.Open] =
        [
            TicketStatus.Triage,
            TicketStatus.InProgress,
            TicketStatus.Closed,
            TicketStatus.Cancelled
        ],
        [TicketStatus.Triage] =
        [
            TicketStatus.Closed,
            TicketStatus.InProgress,
            TicketStatus.WaitingOnRequester,
            TicketStatus.Cancelled
        ],
        [TicketStatus.InProgress] =
        [
            TicketStatus.Closed,
            TicketStatus.Triage,
            TicketStatus.WaitingOnRequester,
            TicketStatus.Resolved,
            TicketStatus.Cancelled
        ],
        [TicketStatus.WaitingOnRequester] =
        [
            TicketStatus.Closed,
            TicketStatus.InProgress,
            TicketStatus.Resolved,
            TicketStatus.Cancelled
        ],
        [TicketStatus.Resolved] =
        [
            TicketStatus.Closed,
            TicketStatus.InProgress
        ],
        // Fechado e cancelado são terminais na versão 1. A reabertura de chamado fechado
        // está no roadmap (README, seção 4.1) e abrirá esta aresta quando existir.
        [TicketStatus.Closed] = [],
        [TicketStatus.Cancelled] = []
    };

    /// <summary>Status que não admitem mais nenhuma transição.</summary>
    public static readonly TicketStatus[] Terminal = [TicketStatus.Closed, TicketStatus.Cancelled];

    public static IReadOnlyList<TicketStatus> AllowedFrom(TicketStatus current) => Allowed[current];

    public static bool CanTransition(TicketStatus from, TicketStatus to) =>
        from != to && Allowed[from].Contains(to);

    /// <summary>
    /// Perfis que podem mover o chamado. O solicitante só cancela o próprio chamado;
    /// a autorização por recurso (é o chamado dele?) é verificada na camada de aplicação.
    /// </summary>
    public static bool IsAllowedForRole(TicketStatus from, TicketStatus to, UserRole role) =>
        CanTransition(from, to) && role switch
        {
            UserRole.Manager => true,
            UserRole.Technician => true,
            UserRole.Requester => to == TicketStatus.Cancelled
                                  || (from == TicketStatus.Resolved && to == TicketStatus.Closed),
            _ => false
        };

    /// <summary>O SLA de resolução fica pausado enquanto o chamado aguarda o solicitante.</summary>
    public static bool PausesResolutionSla(TicketStatus status) => status == TicketStatus.WaitingOnRequester;

    /// <summary>Status que encerram a contagem de SLA de forma definitiva.</summary>
    public static bool EndsSlaClock(TicketStatus status) =>
        status is TicketStatus.Resolved or TicketStatus.Closed or TicketStatus.Cancelled;
}
