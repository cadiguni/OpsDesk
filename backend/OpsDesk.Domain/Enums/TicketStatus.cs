namespace OpsDesk.Domain.Enums;

/// <summary>
/// Ciclo de vida do chamado. Persistido como string.
/// As transições permitidas estão em <see cref="Tickets.TicketStatusMachine"/>.
/// </summary>
public enum TicketStatus
{
    /// <summary>Aberto — status inicial de todo chamado.</summary>
    Open,

    /// <summary>Em triagem — equipe analisando categoria, prioridade e responsável.</summary>
    Triage,

    /// <summary>Em atendimento — técnico assumiu e está trabalhando no chamado.</summary>
    InProgress,

    /// <summary>Aguardando usuário — depende do solicitante. Pausa o SLA de resolução.</summary>
    WaitingOnRequester,

    /// <summary>Resolvido — solução aplicada. Encerra o SLA de resolução.</summary>
    Resolved,

    /// <summary>Fechado — status final.</summary>
    Closed,

    /// <summary>Cancelado — não deve mais ser atendido. Fica fora dos indicadores de SLA.</summary>
    Cancelled
}
