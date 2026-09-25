using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Sla;

/// <summary>
/// Quando um prazo de SLA merece alerta (README, seção 18, versão 1.2).
///
/// O aviso prévio é proporcional ao prazo: sai quando resta a fração configurada — 25% —
/// do prazo da prioridade, em horas úteis. Crítico, com 4 horas de resolução, avisa com 1
/// hora de antecedência; média, com 30, avisa com 7h30. Um valor fixo em horas ou chegaria
/// tarde demais para o crítico ou cedo demais para o de baixa prioridade.
///
/// A conta de horas úteis é do <see cref="IBusinessCalendar"/> (invariante 6): sexta às
/// 17h com vencimento segunda às 9h tem duas horas úteis pela frente, não sessenta e quatro.
/// </summary>
public static class SlaAlertRules
{
    /// <summary>
    /// Vencimento mais antigo que isso não gera alerta de vencido. Sem esse corte, a primeira
    /// rodada depois de o recurso entrar no ar — ou de a tarefa ficar parada — avisaria de
    /// uma vez todos os chamados vencidos da história. Para esses existe o filtro de
    /// vencidos da lista.
    /// </summary>
    public static readonly TimeSpan OverdueLookback = TimeSpan.FromHours(24);

    /// <param name="policyHours">O prazo da prioridade, em horas úteis, como está na política.</param>
    public static SlaAlertStage? Evaluate(
        DateTimeOffset dueAt,
        int policyHours,
        int warningPercent,
        DateTimeOffset now,
        IBusinessCalendar calendar)
    {
        if (now >= dueAt)
        {
            return now - dueAt <= OverdueLookback ? SlaAlertStage.Overdue : null;
        }

        var remaining = calendar.BusinessMinutesBetween(now, dueAt);
        var warningMinutes = policyHours * 60 * warningPercent / 100;

        return remaining <= warningMinutes ? SlaAlertStage.DueSoon : null;
    }
}
