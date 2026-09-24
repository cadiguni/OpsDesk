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
        // Reabertura (README, seção 5.9). A aresta existe sempre; a janela de dias em que
        // ela vale depende do relógio e da configuração, e quem a aplica é a camada de
        // aplicação — o grafo não sabe que horas são.
        [TicketStatus.Closed] =
        [
            TicketStatus.InProgress
        ],
        // Cancelado é terminal: quem cancelou desistiu, e "o problema voltou" não se aplica.
        [TicketStatus.Cancelled] = []
    };

    /// <summary>
    /// Status encerrados: fora das filas de trabalho e fora da conta de "em aberto".
    /// Fechado ainda pode ser reaberto dentro da janela; cancelado, nunca.
    /// </summary>
    public static readonly TicketStatus[] Finished = [TicketStatus.Closed, TicketStatus.Cancelled];

    /// <summary>
    /// Volta de um chamado dado como resolvido para atendimento. É a mesma transição com
    /// ou sem fechamento no meio, e o SLA a trata igual: o tempo parado não conta.
    /// </summary>
    public static bool IsReopening(TicketStatus from, TicketStatus to) =>
        from is TicketStatus.Resolved or TicketStatus.Closed && to == TicketStatus.InProgress;

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

    /// <summary>
    /// Status que encerram a contagem de SLA. Definitivo só para cancelado: resolvido e
    /// fechado retomam a contagem de onde pararam se o chamado for reaberto.
    /// </summary>
    public static bool EndsSlaClock(TicketStatus status) =>
        status is TicketStatus.Resolved or TicketStatus.Closed or TicketStatus.Cancelled;
}
