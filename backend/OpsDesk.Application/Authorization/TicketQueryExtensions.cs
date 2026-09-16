using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Authorization;

/// <summary>
/// Filtro de visibilidade aplicado no <see cref="IQueryable{T}"/>, antes de qualquer projeção.
/// Invariante 1 do CLAUDE.md: toda leitura de chamado passa por aqui. Não existe caminho
/// em que esconder chamado de terceiro dependa de atributo de rota ou de condicional na interface.
/// </summary>
public static class TicketQueryExtensions
{
    /// <summary>
    /// Chamados que o usuário pode enxergar:
    /// gestor vê todos; técnico vê os sem responsável e os atribuídos a ele;
    /// solicitante vê apenas os próprios. O <c>default</c> cai no caso mais restrito
    /// de propósito: perfil novo que ninguém lembrou de tratar não vaza dado.
    /// </summary>
    public static IQueryable<Ticket> VisibleTo(this IQueryable<Ticket> tickets, TicketViewer viewer) =>
        viewer.Role switch
        {
            UserRole.Manager => tickets,
            UserRole.Technician => tickets.Where(t =>
                t.AssignedTechnicianId == null || t.AssignedTechnicianId == viewer.UserId),
            _ => tickets.Where(t => t.RequesterId == viewer.UserId)
        };

    /// <summary>
    /// Comentários que o usuário pode enxergar. Invariante 2 do CLAUDE.md: comentário com
    /// <c>IsInternal = true</c> não sai daqui para quem não é da equipe.
    /// </summary>
    public static IQueryable<TicketComment> VisibleTo(
        this IQueryable<TicketComment> comments, TicketViewer viewer) =>
        viewer.Role switch
        {
            UserRole.Manager => comments,
            UserRole.Technician => comments.Where(c =>
                c.Ticket.AssignedTechnicianId == null || c.Ticket.AssignedTechnicianId == viewer.UserId),
            _ => comments.Where(c => !c.IsInternal && c.Ticket.RequesterId == viewer.UserId)
        };

    /// <summary>Histórico segue a visibilidade do chamado a que pertence.</summary>
    public static IQueryable<TicketHistory> VisibleTo(
        this IQueryable<TicketHistory> history, TicketViewer viewer) =>
        viewer.Role switch
        {
            UserRole.Manager => history,
            UserRole.Technician => history.Where(h =>
                h.Ticket.AssignedTechnicianId == null || h.Ticket.AssignedTechnicianId == viewer.UserId),
            _ => history.Where(h => h.Ticket.RequesterId == viewer.UserId)
        };

    /// <summary>
    /// Comentários de um chamado já filtrados por visibilidade. Atalho para o caso mais
    /// comum, que é a tela de detalhe.
    /// </summary>
    public static IQueryable<TicketComment> OfTicketVisibleTo(
        this IQueryable<TicketComment> comments, Guid ticketId, TicketViewer viewer) =>
        comments.Where(c => c.TicketId == ticketId).VisibleTo(viewer);
}
