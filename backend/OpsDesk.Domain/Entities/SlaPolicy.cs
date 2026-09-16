using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Prazo de resposta e de resolução de uma prioridade, em horas úteis.
/// Uma política ativa por prioridade.
/// </summary>
public class SlaPolicy : IHasUpdatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public TicketPriority Priority { get; set; }

    /// <summary>Horas úteis até o primeiro retorno da equipe.</summary>
    public int ResponseHours { get; set; }

    /// <summary>Horas úteis até o chamado ser resolvido, descontadas as pausas.</summary>
    public int ResolutionHours { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
