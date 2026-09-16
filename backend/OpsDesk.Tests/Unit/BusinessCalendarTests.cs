using Microsoft.Extensions.Options;
using OpsDesk.Application.Abstractions;
using OpsDesk.Infrastructure.Time;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// Horas úteis é a parte genuinamente complicada da versão 1, e a que mais gera bug
/// silencioso. Todos os casos abaixo são datados em uma semana escolhida de propósito:
/// 01/06/2026 é segunda-feira e 04/06/2026 é Corpus Christi, feriado no meio da semana.
/// Assim virada de expediente, fim de semana e feriado aparecem na mesma janela.
/// </summary>
public class BusinessCalendarTests
{
    /// <summary>America/Sao_Paulo não tem horário de verão desde 2019: UTC-3 o ano inteiro.</summary>
    private static readonly TimeSpan SaoPaulo = TimeSpan.FromHours(-3);

    private static readonly DateOnly CorpusChristi = new(2026, 6, 4);

    private static IBusinessCalendar Calendar(params DateOnly[] holidays) =>
        new BusinessCalendar(
            Options.Create(new BusinessHoursOptions()),
            new FixedHolidayProvider(holidays));

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 6, day, hour, minute, 0, SaoPaulo);

    // ----- AddBusinessHours -----

    [Fact]
    public void Soma_dentro_do_mesmo_dia()
    {
        var due = Calendar().AddBusinessHours(At(1, 9), 4);

        Assert.Equal(At(1, 13), due);
    }

    [Fact]
    public void Vira_para_o_dia_seguinte_quando_nao_cabe_no_expediente()
    {
        // Segunda 16:00 + 4h: duas horas até as 18:00, as outras duas na terça.
        var due = Calendar().AddBusinessHours(At(1, 16), 4);

        Assert.Equal(At(2, 10), due);
    }

    [Fact]
    public void Chamado_aberto_antes_da_abertura_conta_a_partir_das_oito()
    {
        var due = Calendar().AddBusinessHours(At(1, 6), 1);

        Assert.Equal(At(1, 9), due);
    }

    [Fact]
    public void Chamado_aberto_depois_do_expediente_conta_no_dia_seguinte()
    {
        var due = Calendar().AddBusinessHours(At(1, 19), 1);

        Assert.Equal(At(2, 9), due);
    }

    [Fact]
    public void Pula_o_fim_de_semana()
    {
        // Sexta 17:00 + 2h: uma hora na sexta, a outra na segunda.
        var due = Calendar().AddBusinessHours(At(5, 17), 2);

        Assert.Equal(At(8, 9), due);
    }

    [Fact]
    public void Chamado_aberto_no_sabado_comeca_na_segunda()
    {
        var due = Calendar().AddBusinessHours(At(6, 10), 1);

        Assert.Equal(At(8, 9), due);
    }

    [Fact]
    public void Pula_o_feriado_no_meio_da_semana()
    {
        // Quarta 17:00 + 2h: uma hora na quarta; quinta é Corpus Christi; sobra a sexta.
        var due = Calendar(CorpusChristi).AddBusinessHours(At(3, 17), 2);

        Assert.Equal(At(5, 9), due);
    }

    [Fact]
    public void Prazo_pode_cair_exatamente_no_fechamento()
    {
        // Um dia útil inteiro: 08:00 mais dez horas dá 18:00 do mesmo dia.
        var due = Calendar().AddBusinessHours(At(1, 8), 10);

        Assert.Equal(At(1, 18), due);
    }

    [Fact]
    public void Chamado_critico_aberto_as_17h59_de_sexta()
    {
        // O caso clássico: sobra um minuto de sexta e o resto do prazo cai na segunda.
        var due = Calendar().AddBusinessHours(At(5, 17, 59), 4);

        Assert.Equal(At(8, 11, 59), due);
    }

    [Fact]
    public void Somar_zero_devolve_o_mesmo_instante()
    {
        // Não normaliza para a próxima abertura: normalizar faria a retomada de uma pausa
        // de zero minuto empurrar um prazo que caiu às 18:00 para o dia seguinte.
        var closing = At(1, 18);

        Assert.Equal(closing, Calendar().AddBusinessMinutes(closing, 0));
    }

    // ----- BusinessMinutesBetween -----

    [Fact]
    public void Minutos_uteis_no_mesmo_dia()
    {
        var minutes = Calendar().BusinessMinutesBetween(At(1, 9), At(1, 11, 30));

        Assert.Equal(150, minutes);
    }

    [Fact]
    public void Minutos_uteis_ignoram_a_noite()
    {
        // Segunda 17:00 até terça 09:00: uma hora de cada lado da noite.
        var minutes = Calendar().BusinessMinutesBetween(At(1, 17), At(2, 9));

        Assert.Equal(120, minutes);
    }

    [Fact]
    public void Minutos_uteis_ignoram_o_fim_de_semana()
    {
        var minutes = Calendar().BusinessMinutesBetween(At(5, 17), At(8, 9));

        Assert.Equal(120, minutes);
    }

    [Fact]
    public void Minutos_uteis_ignoram_o_feriado()
    {
        var minutes = Calendar(CorpusChristi).BusinessMinutesBetween(At(3, 17), At(5, 9));

        Assert.Equal(120, minutes);
    }

    [Fact]
    public void Intervalo_inteiro_fora_do_expediente_nao_conta_nada()
    {
        var minutes = Calendar().BusinessMinutesBetween(At(6, 10), At(7, 10));

        Assert.Equal(0, minutes);
    }

    [Fact]
    public void Intervalo_invertido_nao_conta_nada()
    {
        var minutes = Calendar().BusinessMinutesBetween(At(2, 10), At(1, 10));

        Assert.Equal(0, minutes);
    }

    [Fact]
    public void Semana_inteira_de_expediente_soma_cinquenta_horas()
    {
        // Segunda 08:00 a sexta 18:00, sem feriado: cinco dias de dez horas.
        var minutes = Calendar().BusinessMinutesBetween(At(1, 8), At(5, 18));

        Assert.Equal(50 * 60, minutes);
    }

    [Fact]
    public void Semana_com_feriado_perde_um_dia_de_expediente()
    {
        var minutes = Calendar(CorpusChristi).BusinessMinutesBetween(At(1, 8), At(5, 18));

        Assert.Equal(40 * 60, minutes);
    }

    // ----- Consultas de expediente -----

    [Theory]
    [InlineData(1, true)]   // segunda
    [InlineData(5, true)]   // sexta
    [InlineData(6, false)]  // sábado
    [InlineData(7, false)]  // domingo
    [InlineData(4, false)]  // Corpus Christi
    public void Reconhece_dia_util(int day, bool expected)
    {
        var isBusinessDay = Calendar(CorpusChristi).IsBusinessDay(new DateOnly(2026, 6, day));

        Assert.Equal(expected, isBusinessDay);
    }

    [Theory]
    [InlineData(1, 7, false)]   // antes da abertura
    [InlineData(1, 8, true)]    // abertura
    [InlineData(1, 17, true)]
    [InlineData(1, 18, false)]  // fechamento não está dentro do expediente
    [InlineData(6, 10, false)]  // sábado
    public void Reconhece_instante_dentro_do_expediente(int day, int hour, bool expected)
    {
        var isWithin = Calendar(CorpusChristi).IsWithinBusinessHours(At(day, hour));

        Assert.Equal(expected, isWithin);
    }

    [Fact]
    public void A_conta_usa_o_fuso_do_expediente_e_nao_UTC()
    {
        // 11:00 UTC é 08:00 em São Paulo. Se a conta fosse feita em UTC, este instante
        // cairia fora do expediente e o prazo seria empurrado indevidamente.
        var eightAmLocalAsUtc = new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero);

        var due = Calendar().AddBusinessHours(eightAmLocalAsUtc, 1);

        Assert.Equal(At(1, 9), due);
    }

    [Fact]
    public void O_prazo_volta_em_UTC_porque_e_assim_que_o_banco_aceita()
    {
        // O Npgsql recusa DateTimeOffset com deslocamento diferente de zero em coluna
        // timestamptz. Devolver o prazo com -03:00 estouraria na gravação do chamado.
        var due = Calendar().AddBusinessHours(At(1, 9), 4);

        Assert.Equal(TimeSpan.Zero, due.Offset);
        Assert.Equal(At(1, 13), due);
    }

    [Fact]
    public void Soma_negativa_e_rejeitada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar().AddBusinessMinutes(At(1, 9), -1));
    }
}
