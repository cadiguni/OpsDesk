using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Tickets;

/// <summary>Transição de status recusada pela máquina de estados.</summary>
public class TicketStatusTransitionException(TicketStatus from, TicketStatus to)
    : InvalidOperationException($"Transição de status inválida: {from} -> {to}.")
{
    public TicketStatus From { get; } = from;

    public TicketStatus To { get; } = to;
}
