namespace OpsDesk.Domain.Enums;

/// <summary>Prioridade do chamado. Persistida como string. Define o SLA aplicado.</summary>
public enum TicketPriority
{
    /// <summary>Baixa — sem impacto relevante na operação.</summary>
    Low,

    /// <summary>Média — impacto parcial, com contorno disponível.</summary>
    Medium,

    /// <summary>Alta — impacto direto no trabalho do usuário ou de uma área.</summary>
    High,

    /// <summary>Crítica — impacto amplo ou impeditivo.</summary>
    Critical
}
