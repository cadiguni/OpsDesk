using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Registro de que um alerta de SLA já foi dado, para não repeti-lo a cada rodada.
///
/// O prazo entra na chave (<see cref="DueAt"/>) de propósito: quando ele se move — pausa
/// encerrada, reabertura, reclassificação —, o alerta do prazo novo é outro alerta, e
/// precisa poder sair. Único por chamado, prazo, estágio e data, no banco: com duas
/// réplicas rodando a verificação ao mesmo tempo, é o índice que impede o aviso em dobro.
/// </summary>
public class SlaAlert : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TicketId { get; set; }

    public SlaDeadline Deadline { get; set; }

    public SlaAlertStage Stage { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
