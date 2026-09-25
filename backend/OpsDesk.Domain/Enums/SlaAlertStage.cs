namespace OpsDesk.Domain.Enums;

/// <summary>Momento do alerta de SLA. Persistido como string.</summary>
public enum SlaAlertStage
{
    /// <summary>Resta pouco do prazo.</summary>
    DueSoon,

    /// <summary>O prazo passou.</summary>
    Overdue
}
