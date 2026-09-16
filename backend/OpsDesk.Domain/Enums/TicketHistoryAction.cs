namespace OpsDesk.Domain.Enums;

/// <summary>Tipo de evento registrado em <c>TicketHistory</c>. Persistido como string.</summary>
public enum TicketHistoryAction
{
    Created,
    StatusChanged,
    PriorityChanged,
    CategoryChanged,
    TechnicianAssigned,
    TechnicianUnassigned,
    CommentAdded,
    InternalCommentAdded,
    Resolved,
    Closed,
    Cancelled
}
