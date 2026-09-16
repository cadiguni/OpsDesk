using Microsoft.Extensions.Options;
using OpsDesk.Application.Abstractions;

namespace OpsDesk.Infrastructure.Time;

/// <summary>
/// Cálculo de horas úteis (decisão 4.5 de docs/arquitetura.md).
///
/// A conta acontece no fuso do expediente, não em UTC: "18:00" é um horário local, e
/// somar horas em UTC erraria por conta do deslocamento. Os limites da API são sempre
/// instantes absolutos, e a conversão é interna.
/// </summary>
public class BusinessCalendar(IOptions<BusinessHoursOptions> options, IHolidayProvider holidays)
    : IBusinessCalendar
{
    private readonly BusinessHoursOptions _options = options.Value;
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public DateTimeOffset AddBusinessHours(DateTimeOffset start, int hours) =>
        AddBusinessMinutes(start, hours * 60);

    public DateTimeOffset AddBusinessMinutes(DateTimeOffset start, int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);

        // Somar zero devolve o mesmo instante, sem normalizar para a próxima abertura.
        // Normalizar aqui empurraria um prazo que caiu exatamente às 18:00 para o dia
        // seguinte, o que faria a retomada de uma pausa de zero minuto dar um dia de brinde.
        if (minutes == 0)
        {
            return start.ToUniversalTime();
        }

        // Um instante fora do expediente não "gasta" tempo: a contagem começa na
        // próxima abertura. Chamado aberto às 23h de sexta começa a contar 8h de segunda.
        var cursor = NextBusinessMoment(ToLocal(start));
        var remaining = minutes;

        while (remaining > 0)
        {
            var endOfDay = cursor.Date.Add(_options.End.ToTimeSpan());
            var availableToday = (int)(endOfDay - cursor).TotalMinutes;

            if (remaining <= availableToday)
            {
                cursor = cursor.AddMinutes(remaining);
                break;
            }

            remaining -= availableToday;
            cursor = StartOfNextBusinessDay(cursor.Date);
        }

        return ToInstant(cursor);
    }

    public int BusinessMinutesBetween(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            return 0;
        }

        var from = ToLocal(start);
        var to = ToLocal(end);
        var total = 0;

        for (var day = DateOnly.FromDateTime(from); day <= DateOnly.FromDateTime(to); day = day.AddDays(1))
        {
            if (!IsBusinessDay(day))
            {
                continue;
            }

            var opensAt = day.ToDateTime(_options.Start);
            var closesAt = day.ToDateTime(_options.End);

            // Interseção entre o intervalo pedido e o expediente deste dia.
            var segmentStart = from > opensAt ? from : opensAt;
            var segmentEnd = to < closesAt ? to : closesAt;

            if (segmentEnd > segmentStart)
            {
                total += (int)(segmentEnd - segmentStart).TotalMinutes;
            }
        }

        return total;
    }

    public bool IsWithinBusinessHours(DateTimeOffset instant)
    {
        var local = ToLocal(instant);
        var time = TimeOnly.FromDateTime(local);

        return IsBusinessDay(DateOnly.FromDateTime(local))
               && time >= _options.Start
               && time < _options.End;
    }

    public bool IsBusinessDay(DateOnly date) =>
        _options.WorkingDays.Contains(date.DayOfWeek) && !holidays.GetHolidays().Contains(date);

    /// <summary>Primeiro instante de expediente igual ou posterior a <paramref name="local"/>.</summary>
    private DateTime NextBusinessMoment(DateTime local)
    {
        var day = DateOnly.FromDateTime(local);

        if (IsBusinessDay(day))
        {
            var opensAt = day.ToDateTime(_options.Start);
            var closesAt = day.ToDateTime(_options.End);

            if (local < opensAt)
            {
                return opensAt;
            }

            if (local < closesAt)
            {
                return local;
            }
        }

        return StartOfNextBusinessDay(local.Date);
    }

    private DateTime StartOfNextBusinessDay(DateTime fromDate)
    {
        var day = DateOnly.FromDateTime(fromDate).AddDays(1);

        while (!IsBusinessDay(day))
        {
            day = day.AddDays(1);
        }

        return day.ToDateTime(_options.Start);
    }

    private DateTime ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, _zone).DateTime;

    /// <summary>
    /// Volta para instante absoluto usando o deslocamento vigente naquela data local.
    ///
    /// Não usamos <c>ConvertTimeToUtc</c> porque ele lança em horário inexistente por
    /// virada de horário de verão; o expediente não encosta nessas viradas, mas depender
    /// disso seria uma bomba silenciosa se o expediente mudar.
    ///
    /// O <c>ToUniversalTime</c> no fim não é enfeite: o Npgsql só aceita
    /// <c>DateTimeOffset</c> com deslocamento zero em coluna <c>timestamptz</c>. Devolver
    /// o prazo com deslocamento -03:00 faria toda inserção de chamado estourar na hora de
    /// gravar o SLA. É o instante idêntico, escrito no fuso que o banco exige.
    /// </summary>
    private DateTimeOffset ToInstant(DateTime local) =>
        new DateTimeOffset(local, _zone.GetUtcOffset(local)).ToUniversalTime();
}
