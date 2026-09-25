using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Authorization;
using OpsDesk.Application.Common;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Notifications;

public record NotificationItem(
    Guid Id,
    NotificationKind Kind,
    Guid TicketId,
    string TicketCode,
    string TicketTitle,
    string? ActorName,
    string? Detail,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public record UnreadCount(int Count);

/// <summary>
/// Notificações do próprio usuário, e só dele.
///
/// Além do dono, cada notificação passa pelo filtro de visibilidade do chamado: ela
/// mostra código e título, e quem deixou de enxergar o chamado — um técnico rebaixado a
/// solicitante, por exemplo — não pode continuar lendo isso pelo sino.
/// </summary>
public class NotificationService(IOpsDeskDbContext db, IClock clock)
{
    public Task<PagedResult<NotificationItem>> ListAsync(
        TicketViewer viewer, bool unreadOnly, PageRequest page, CancellationToken cancellationToken = default)
    {
        var query = Mine(viewer).AsNoTracking();

        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        return query
            .OrderByDescending(n => n.CreatedAt)
            .ThenBy(n => n.Id)
            .Select(n => new NotificationItem(
                n.Id,
                n.Kind,
                n.TicketId,
                n.Ticket.Code,
                n.Ticket.Title,
                n.Actor == null ? null : n.Actor.Name,
                n.Detail,
                n.CreatedAt,
                n.ReadAt))
            .ToPagedResultAsync(page, cancellationToken);
    }

    public async Task<UnreadCount> CountUnreadAsync(TicketViewer viewer, CancellationToken cancellationToken = default) =>
        new(await Mine(viewer).CountAsync(n => n.ReadAt == null, cancellationToken));

    /// <returns>Falso quando a notificação não existe ou não é de quem pediu.</returns>
    public async Task<bool> MarkReadAsync(Guid id, TicketViewer viewer, CancellationToken cancellationToken = default)
    {
        var notification = await Mine(viewer).SingleOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (notification is null)
        {
            return false;
        }

        notification.ReadAt ??= clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public Task MarkAllReadAsync(TicketViewer viewer, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        return Mine(viewer)
            .Where(n => n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    private IQueryable<Notification> Mine(TicketViewer viewer)
    {
        var visibleTickets = db.Tickets.VisibleTo(viewer);

        return db.Notifications.Where(n =>
            n.UserId == viewer.UserId && visibleTickets.Any(t => t.Id == n.TicketId));
    }
}
