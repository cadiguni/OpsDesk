namespace OpsDesk.Application.Sla;

/// <summary>Par de prazos calculado na criação do chamado.</summary>
public readonly record struct SlaDeadlines(DateTimeOffset ResponseDueAt, DateTimeOffset ResolutionDueAt);
