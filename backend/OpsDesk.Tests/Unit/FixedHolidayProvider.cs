using OpsDesk.Application.Abstractions;

namespace OpsDesk.Tests.Unit;

/// <summary>Feriados fixos para teste, sem banco no caminho.</summary>
public class FixedHolidayProvider(params DateOnly[] holidays) : IHolidayProvider
{
    private readonly HashSet<DateOnly> _holidays = [.. holidays];

    public IReadOnlySet<DateOnly> GetHolidays() => _holidays;
}
