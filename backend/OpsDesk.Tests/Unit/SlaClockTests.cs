using Microsoft.Extensions.Options;
using OpsDesk.Application.Sla;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Infrastructure.Time;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// Regras de SLA da seção 8 do README, com foco na pausa: sem ela, o indicador de
/// chamados vencidos passa a medir a demora do solicitante em vez do desempenho da TI.
/// </summary>
public class SlaClockTests
{
    private static readonly TimeSpan SaoPaulo = TimeSpan.FromHours(-3);

    private static SlaClock Clock() => new(
        new BusinessCalendar(Options.Create(new BusinessHoursOptions()), new FixedHolidayProvider()));

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 6, day, hour, minute, 0, SaoPaulo);

    private static SlaPolicy Critical() => new()
    {
        Priority = TicketPriority.Critical,
        ResponseHours = 1,
        ResolutionHours = 4
    };

    private static Ticket Paused(DateTimeOffset pausedAt) => new()
    {
        Status = TicketStatus.WaitingOnRequester,
        SlaResponseDueAt = At(1, 10),
        SlaResolutionDueAt = At(1, 13),
        SlaPausedAt = pausedAt
    };

    [Fact]
    public void Calcula_os_dois_prazos_na_criacao()
    {
        var deadlines = Clock().Calculate(At(1, 9), Critical());

        Assert.Equal(At(1, 10), deadlines.ResponseDueAt);
        Assert.Equal(At(1, 13), deadlines.ResolutionDueAt);
    }

    [Fact]
    public void Pausa_registra_o_instante()
    {
        var ticket = new Ticket();

        Clock().Pause(ticket, At(1, 10));

        Assert.Equal(At(1, 10), ticket.SlaPausedAt);
    }

    [Fact]
    public void Pausar_duas_vezes_nao_move_o_instante_da_pausa()
    {
        // Se movesse, cada ida e volta do status zeraria o tempo já acumulado em espera.
        var ticket = new Ticket();
        var clock = Clock();

        clock.Pause(ticket, At(1, 10));
        clock.Pause(ticket, At(1, 11));

        Assert.Equal(At(1, 10), ticket.SlaPausedAt);
    }

    [Fact]
    public void Retomada_empurra_o_prazo_de_resolucao_pelo_tempo_util_em_espera()
    {
        var ticket = Paused(At(1, 10));

        var pausedMinutes = Clock().Resume(ticket, At(1, 12));

        Assert.Equal(120, pausedMinutes);
        Assert.Equal(120, ticket.SlaPausedBusinessMinutes);
        Assert.Equal(At(1, 15), ticket.SlaResolutionDueAt);
        Assert.Null(ticket.SlaPausedAt);
    }

    [Fact]
    public void Retomada_nao_toca_o_prazo_de_resposta()
    {
        // O SLA de resposta mede o tempo até o primeiro retorno da equipe. Esperar o
        // solicitante não é desculpa para não ter respondido. README, seção 8.2.
        var ticket = Paused(At(1, 10));
        var responseDue = ticket.SlaResponseDueAt;

        Clock().Resume(ticket, At(1, 12));

        Assert.Equal(responseDue, ticket.SlaResponseDueAt);
    }

    [Fact]
    public void Pausa_atravessando_a_noite_conta_so_o_expediente()
    {
        // Segunda 17:00 a terça 09:00: duas horas úteis, não dezesseis.
        var ticket = Paused(At(1, 17));

        var pausedMinutes = Clock().Resume(ticket, At(2, 9));

        Assert.Equal(120, pausedMinutes);
        Assert.Equal(At(1, 15), ticket.SlaResolutionDueAt);
    }

    [Fact]
    public void Pausa_atravessando_o_fim_de_semana_conta_so_o_expediente()
    {
        var ticket = Paused(At(5, 17));

        var pausedMinutes = Clock().Resume(ticket, At(8, 9));

        Assert.Equal(120, pausedMinutes);
    }

    [Fact]
    public void Pausa_inteira_fora_do_expediente_nao_muda_o_prazo()
    {
        var ticket = Paused(At(6, 10));
        var resolutionDue = ticket.SlaResolutionDueAt;

        var pausedMinutes = Clock().Resume(ticket, At(7, 10));

        Assert.Equal(0, pausedMinutes);
        Assert.Equal(resolutionDue, ticket.SlaResolutionDueAt);
        Assert.Null(ticket.SlaPausedAt);
    }

    [Fact]
    public void Retomar_chamado_que_nao_estava_pausado_nao_faz_nada()
    {
        var ticket = new Ticket
        {
            SlaResolutionDueAt = At(1, 13),
            SlaPausedBusinessMinutes = 0
        };

        var pausedMinutes = Clock().Resume(ticket, At(1, 12));

        Assert.Equal(0, pausedMinutes);
        Assert.Equal(At(1, 13), ticket.SlaResolutionDueAt);
    }

    [Fact]
    public void Pausas_sucessivas_acumulam()
    {
        var ticket = Paused(At(1, 9));
        var clock = Clock();

        clock.Resume(ticket, At(1, 10));  // 60 minutos
        clock.Pause(ticket, At(1, 11));
        clock.Resume(ticket, At(1, 12));  // outros 60

        Assert.Equal(120, ticket.SlaPausedBusinessMinutes);
        Assert.Equal(At(1, 15), ticket.SlaResolutionDueAt);
    }

    [Theory]
    [InlineData(TicketStatus.Open, false)]
    [InlineData(TicketStatus.Resolved, false)]
    [InlineData(TicketStatus.Cancelled, true)]
    public void Chamado_cancelado_fica_fora_dos_indicadores_de_sla(TicketStatus status, bool excluded)
    {
        var ticket = new Ticket { Status = status };

        Assert.Equal(excluded, !ticket.CountsForSla);
    }

    [Fact]
    public void Chamado_vencido_e_o_que_passou_do_prazo_sem_atingir_o_marco()
    {
        var ticket = new Ticket
        {
            SlaResponseDueAt = At(1, 10),
            SlaResolutionDueAt = At(1, 13)
        };

        Assert.True(ticket.IsResponseOverdue(At(1, 11)));
        Assert.True(ticket.IsResolutionOverdue(At(1, 14)));

        ticket.FirstRespondedAt = At(1, 9);
        ticket.ResolvedAt = At(1, 12);

        Assert.False(ticket.IsResponseOverdue(At(1, 11)));
        Assert.False(ticket.IsResolutionOverdue(At(1, 14)));
    }

    [Fact]
    public void Chamado_cancelado_nunca_aparece_como_vencido()
    {
        var ticket = new Ticket
        {
            Status = TicketStatus.Cancelled,
            SlaResponseDueAt = At(1, 10),
            SlaResolutionDueAt = At(1, 13)
        };

        Assert.False(ticket.IsResponseOverdue(At(30, 10)));
        Assert.False(ticket.IsResolutionOverdue(At(30, 10)));
    }

    // ----- Reabertura -----

    private static Ticket Resolved(DateTimeOffset resolvedAt, DateTimeOffset resolutionDueAt) => new()
    {
        Status = TicketStatus.Resolved,
        SlaResponseDueAt = At(1, 10),
        SlaResolutionDueAt = resolutionDueAt,
        ResolvedAt = resolvedAt
    };

    [Fact]
    public void Reabertura_devolve_o_tempo_que_faltava_quando_resolveu()
    {
        // Resolvido segunda às 10:00 com prazo às 13:00: faltavam três horas úteis.
        // Reaberto quarta às 10:00, as mesmas três horas levam o prazo para 13:00 de quarta.
        var ticket = Resolved(At(1, 10), At(1, 13));

        var stoppedMinutes = Clock().ResumeAfterReopening(ticket, At(3, 10));

        Assert.Equal(20 * 60, stoppedMinutes);
        Assert.Equal(20 * 60, ticket.SlaPausedBusinessMinutes);
        Assert.Equal(At(3, 13), ticket.SlaResolutionDueAt);
    }

    [Fact]
    public void Chamado_resolvido_ja_vencido_volta_vencido()
    {
        // O atraso aconteceu antes da resolução e foi real; reabrir não o apaga.
        var ticket = Resolved(At(1, 15), At(1, 13));

        Clock().ResumeAfterReopening(ticket, At(2, 9));

        Assert.Equal(At(1, 17), ticket.SlaResolutionDueAt);
        Assert.True(ticket.SlaResolutionDueAt < At(2, 9));
    }

    [Fact]
    public void Reabertura_fora_do_expediente_nao_mexe_no_prazo()
    {
        var ticket = Resolved(At(1, 18, 30), At(2, 12));

        var stoppedMinutes = Clock().ResumeAfterReopening(ticket, At(1, 20));

        Assert.Equal(0, stoppedMinutes);
        Assert.Equal(At(2, 12), ticket.SlaResolutionDueAt);
    }

    [Fact]
    public void Reabertura_soma_ao_tempo_ja_pausado()
    {
        // A espera pelo solicitante e o tempo parado depois da resolução são o mesmo tipo
        // de tempo: nenhum dos dois é da equipe. A reclassificação reaplica os dois juntos.
        var ticket = Resolved(At(1, 10), At(1, 13));
        ticket.SlaPausedBusinessMinutes = 30;

        Clock().ResumeAfterReopening(ticket, At(1, 11));

        Assert.Equal(90, ticket.SlaPausedBusinessMinutes);
    }
}
