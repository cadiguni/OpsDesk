namespace OpsDesk.Application.Abstractions;

/// <summary>
/// Fonte de tempo injetável. Serviço nenhum chama <c>DateTimeOffset.UtcNow</c> direto,
/// para que SLA e histórico sejam testáveis com instantes fixos.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
