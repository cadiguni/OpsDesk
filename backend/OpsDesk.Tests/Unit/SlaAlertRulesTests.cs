using Microsoft.Extensions.Options;
using OpsDesk.Application.Sla;
using OpsDesk.Domain.Enums;
using OpsDesk.Infrastructure.Time;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// Quando o alerta de SLA sai: com 25% do prazo restante, em horas úteis, e ao vencer —
/// mas não por vencimento antigo.
/// </summary>
public class SlaAlertRulesTests
{
    private static readonly TimeSpan SaoPaulo = TimeSpan.FromHours(-3);

    private static readonly BusinessCalendar Calendar =
        new(Options.Create(new BusinessHoursOptions()), new FixedHolidayProvider());

    /// <summary>Junho de 2026: dia 1 é segunda-feira.</summary>
    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 6, day, hour, minute, 0, SaoPaulo);

    private static SlaAlertStage? Evaluate(DateTimeOffset due, int hours, DateTimeOffset now) =>
        SlaAlertRules.Evaluate(due, hours, warningPercent: 25, now, Calendar);

    [Fact]
    public void Critico_avisa_com_uma_hora_util_de_antecedencia()
    {
        // Resolução crítica: 4 horas úteis. 25% é uma hora.
        Assert.Null(Evaluate(At(1, 13), 4, At(1, 11, 59)));
        Assert.Equal(SlaAlertStage.DueSoon, Evaluate(At(1, 13), 4, At(1, 12)));
    }

    [Fact]
    public void Prioridade_media_avisa_com_sete_horas_e_meia_uteis()
    {
        // Resolução média: 30 horas úteis. 25% são 450 minutos.
        var due = At(3, 18);

        Assert.Null(Evaluate(due, 30, At(3, 10, 29)));
        Assert.Equal(SlaAlertStage.DueSoon, Evaluate(due, 30, At(3, 10, 30)));
    }

    [Fact]
    public void Fim_de_semana_nao_conta_como_tempo_restante()
    {
        // Sexta às 17h, vencimento segunda às 9h: duas horas úteis, não sessenta e quatro
        // corridas. Com prazo de 8 horas, 25% são duas horas — é hora de avisar.
        Assert.Equal(SlaAlertStage.DueSoon, Evaluate(At(8, 9), 8, At(5, 17)));
    }

    [Fact]
    public void Prazo_vencido_ha_pouco_alerta_como_vencido()
    {
        Assert.Equal(SlaAlertStage.Overdue, Evaluate(At(1, 13), 4, At(1, 13)));
        Assert.Equal(SlaAlertStage.Overdue, Evaluate(At(1, 13), 4, At(2, 12)));
    }

    [Fact]
    public void Vencimento_antigo_nao_gera_alerta()
    {
        // A primeira rodada depois de o recurso entrar no ar não pode avisar de uma vez
        // todos os chamados vencidos da história.
        Assert.Null(Evaluate(At(1, 13), 4, At(2, 14)));
    }
}
