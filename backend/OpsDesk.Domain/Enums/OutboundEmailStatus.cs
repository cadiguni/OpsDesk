namespace OpsDesk.Domain.Enums;

/// <summary>Situação de um e-mail na fila de saída. Persistido como string.</summary>
public enum OutboundEmailStatus
{
    Pending,
    Sent,

    /// <summary>Esgotou as tentativas. Fica na tabela para diagnóstico.</summary>
    Failed
}
