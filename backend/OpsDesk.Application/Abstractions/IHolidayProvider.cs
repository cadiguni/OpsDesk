namespace OpsDesk.Application.Abstractions;

/// <summary>
/// Feriados que saem da contagem de horas úteis. As datas são locais ao fuso do
/// expediente, nunca instantes UTC.
/// </summary>
public interface IHolidayProvider
{
    IReadOnlySet<DateOnly> GetHolidays();
}
