using OpsDesk.Domain.Common;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Comentário em um chamado. Quando <see cref="IsInternal"/> é <c>true</c>, o comentário
/// nunca pode chegar ao solicitante — nem por API, nem por e-mail, nem por notificação.
/// </summary>
public class TicketComment : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public Guid AuthorId { get; set; }

    public User Author { get; set; } = null!;

    public string Content { get; set; } = null!;

    /// <summary>Visível apenas para técnicos e gestores.</summary>
    public bool IsInternal { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
