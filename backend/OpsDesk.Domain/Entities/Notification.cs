using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Notificação no portal, para o responsável pelo chamado (README, seção 18, versão 1.2).
///
/// Guarda o tipo e quem agiu, e não um texto pronto: o texto é da interface, em português,
/// como o do histórico. Código e título do chamado são lidos na hora, pelo vínculo.
/// </summary>
public class Notification : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public Guid TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public NotificationKind Kind { get; set; }

    /// <summary>Quem fez a ação. Nulo quando foi o próprio sistema.</summary>
    public Guid? ActorId { get; set; }

    public User? Actor { get; set; }

    /// <summary>Complemento do tipo — hoje, o status novo em <see cref="NotificationKind.StatusChanged"/>.</summary>
    public string? Detail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}
