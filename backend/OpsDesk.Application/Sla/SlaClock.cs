using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Application.Sla;

/// <summary>
/// Aplica as regras de SLA da seção 8 do README sobre um chamado. Toda conta de tempo
/// é delegada ao <see cref="IBusinessCalendar"/>; esta classe só decide o que contar.
///
/// Não existe processo em segundo plano recalculando prazos: o prazo é gravado na criação
/// e só muda quando a pausa é encerrada.
/// </summary>
public class SlaClock(IBusinessCalendar calendar)
{
    /// <summary>Prazos de resposta e resolução a partir da criação do chamado.</summary>
    public SlaDeadlines Calculate(DateTimeOffset createdAt, SlaPolicy policy) => new(
        calendar.AddBusinessHours(createdAt, policy.ResponseHours),
        calendar.AddBusinessHours(createdAt, policy.ResolutionHours));

    /// <summary>
    /// Registra o início da pausa, ao entrar em "Aguardando usuário".
    /// Chamar duas vezes seguidas não move o instante da pausa: o primeiro vale.
    /// </summary>
    public void Pause(Ticket ticket, DateTimeOffset pausedAt) =>
        ticket.SlaPausedAt ??= pausedAt;

    /// <summary>
    /// Encerra a pausa e empurra o prazo de resolução pelo tempo útil que o chamado
    /// passou aguardando o solicitante.
    ///
    /// O prazo de resposta não é tocado de propósito: ele mede o tempo até o primeiro
    /// retorno da equipe, e esperar o solicitante não é desculpa para não ter respondido.
    /// </summary>
    /// <returns>Minutos úteis acrescentados nesta retomada.</returns>
    public int Resume(Ticket ticket, DateTimeOffset resumedAt)
    {
        if (ticket.SlaPausedAt is not { } pausedAt)
        {
            return 0;
        }

        var pausedMinutes = calendar.BusinessMinutesBetween(pausedAt, resumedAt);

        if (pausedMinutes == 0)
        {
            // Pausa iniciada e encerrada fora do expediente não consumiu tempo útil.
            // Encerramos a pausa sem mexer no prazo.
            ticket.SlaPausedAt = null;
            return 0;
        }

        ticket.SlaPausedBusinessMinutes += pausedMinutes;
        ticket.SlaResolutionDueAt = calendar.AddBusinessMinutes(ticket.SlaResolutionDueAt, pausedMinutes);
        ticket.SlaPausedAt = null;

        return pausedMinutes;
    }
}
