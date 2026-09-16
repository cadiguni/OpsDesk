namespace OpsDesk.Infrastructure.Time;

/// <summary>
/// Expediente usado no cálculo de horas úteis. Fixo na versão 1 — não é configurável
/// pela interface, só por <c>appsettings</c>.
/// </summary>
public class BusinessHoursOptions
{
    public const string SectionName = "BusinessHours";

    /// <summary>Identificador IANA do fuso do expediente.</summary>
    public string TimeZone { get; set; } = "America/Sao_Paulo";

    public TimeOnly Start { get; set; } = new(8, 0);

    public TimeOnly End { get; set; } = new(18, 0);

    /// <summary>Dias em que a equipe atende. Padrão: segunda a sexta.</summary>
    public DayOfWeek[] WorkingDays { get; set; } =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    ];

    public int MinutesPerDay => (int)(End - Start).TotalMinutes;
}
