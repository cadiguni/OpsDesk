namespace OpsDesk.Application.Abstractions;

/// <summary>
/// Única casa do cálculo de horas úteis. Nenhum serviço replica esta lógica.
/// Todos os parâmetros e retornos são instantes absolutos; a conversão para o fuso do
/// expediente acontece aqui dentro.
/// </summary>
public interface IBusinessCalendar
{
    /// <summary>
    /// Prazo obtido somando <paramref name="hours"/> horas úteis a <paramref name="start"/>.
    /// Se <paramref name="start"/> cai fora do expediente, a contagem começa na próxima
    /// abertura — chamado aberto às 23h de sexta começa a contar às 8h de segunda.
    /// </summary>
    DateTimeOffset AddBusinessHours(DateTimeOffset start, int hours);

    /// <summary>Mesma regra de <see cref="AddBusinessHours"/>, na granularidade de minutos.</summary>
    DateTimeOffset AddBusinessMinutes(DateTimeOffset start, int minutes);

    /// <summary>
    /// Minutos úteis decorridos entre dois instantes, ignorando noites, fins de semana
    /// e feriados. Zero quando <paramref name="end"/> não é posterior a <paramref name="start"/>.
    /// </summary>
    int BusinessMinutesBetween(DateTimeOffset start, DateTimeOffset end);

    /// <summary>Se o instante cai dentro do expediente.</summary>
    bool IsWithinBusinessHours(DateTimeOffset instant);

    /// <summary>Se a data, no fuso do expediente, é dia útil.</summary>
    bool IsBusinessDay(DateOnly date);
}
