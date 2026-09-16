using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Evento de auditoria do chamado. Append-only: nada de <c>Update</c> nem <c>Delete</c>.
/// O preenchimento é feito pelo interceptor do EF Core, não pelos serviços.
/// </summary>
public class TicketHistory : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    /// <summary>Autor da ação. Nulo apenas quando a mudança vem de rotina do sistema.</summary>
    public Guid? ChangedById { get; set; }

    public User? ChangedBy { get; set; }

    public TicketHistoryAction Action { get; set; }

    public string? PreviousValue { get; set; }

    public string? NewValue { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
