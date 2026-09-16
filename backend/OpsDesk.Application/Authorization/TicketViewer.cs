using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Authorization;

/// <summary>
/// Identidade do usuário autenticado reduzida ao que a autorização precisa saber.
/// É um valor, não um serviço, para que o filtro de visibilidade seja testável sem DI.
/// </summary>
public readonly record struct TicketViewer(Guid UserId, UserRole Role)
{
    public bool IsStaff => Role is UserRole.Technician or UserRole.Manager;

    public bool IsManager => Role == UserRole.Manager;

    public static TicketViewer From(User user) => new(user.Id, user.Role);
}
