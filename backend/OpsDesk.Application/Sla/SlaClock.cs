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
    /// Recalcula os prazos depois de uma troca de prioridade (README, seção 6.1).
    ///
    /// A conta recomeça da <b>abertura</b> do chamado, não do instante da reclassificação:
    /// o prazo expressa o compromisso com aquele chamado desde que ele existe. Contar da
    /// reclassificação daria mais folga a um chamado promovido a crítico do que a um
    /// aberto como crítico, e a promoção viraria uma forma de ganhar prazo.
    ///
    /// Dois marcos já cumpridos não são reescritos:
    ///
    /// <list type="bullet">
    /// <item>o prazo de resposta só muda enquanto não houve primeira resposta — mexer
    /// depois transformaria retroativamente um atendimento pontual em atrasado, ou o
    /// contrário;</item>
    /// <item>o prazo de resolução só muda enquanto o chamado não foi resolvido.</item>
    /// </list>
    ///
    /// O tempo já gasto aguardando o solicitante é reaplicado: a pausa não se perde na
    /// reclassificação. Pausa em curso não precisa de tratamento aqui — ela é somada pelo
    /// <see cref="Resume"/>, a partir de <c>SlaPausedAt</c>.
    /// </summary>
    public void Recalculate(Ticket ticket, SlaPolicy policy)
    {
        var deadlines = Calculate(ticket.CreatedAt, policy);

        if (ticket.FirstRespondedAt is null)
        {
            ticket.SlaResponseDueAt = deadlines.ResponseDueAt;
        }

        if (ticket.ResolvedAt is null)
        {
            ticket.SlaResolutionDueAt = ticket.SlaPausedBusinessMinutes > 0
                ? calendar.AddBusinessMinutes(
                    deadlines.ResolutionDueAt, ticket.SlaPausedBusinessMinutes)
                : deadlines.ResolutionDueAt;
        }
    }

    /// <summary>
    /// Retoma o SLA de resolução na reabertura, de onde ele parou (README, seção 5.9).
    ///
    /// O tempo útil entre a resolução e a reabertura entra como pausa: soma-se ao prazo e
    /// ao acumulado de <c>SlaPausedBusinessMinutes</c>, exatamente como a espera pelo
    /// solicitante. Assim o chamado volta com o tempo que ainda tinha — e um chamado que
    /// já estava vencido quando foi resolvido volta vencido, porque o atraso foi real.
    ///
    /// Recomeçar do zero daria prazo cheio a cada reabertura, e o compromisso assumido na
    /// abertura deixaria de valer. Não fazer nada, o comportamento antigo, fazia o chamado
    /// voltar vencido pelo tempo em que a equipe não tinha como agir.
    /// </summary>
    /// <returns>Minutos úteis acrescentados nesta retomada.</returns>
    public int ResumeAfterReopening(Ticket ticket, DateTimeOffset reopenedAt)
    {
        if (ticket.ResolvedAt is not { } resolvedAt)
        {
            return 0;
        }

        var stoppedMinutes = calendar.BusinessMinutesBetween(resolvedAt, reopenedAt);

        if (stoppedMinutes > 0)
        {
            ticket.SlaPausedBusinessMinutes += stoppedMinutes;
            ticket.SlaResolutionDueAt = calendar.AddBusinessMinutes(ticket.SlaResolutionDueAt, stoppedMinutes);
        }

        return stoppedMinutes;
    }

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
