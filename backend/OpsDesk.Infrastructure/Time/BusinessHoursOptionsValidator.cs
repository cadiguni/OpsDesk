using Microsoft.Extensions.Options;

namespace OpsDesk.Infrastructure.Time;

/// <summary>
/// Valida o expediente na subida.
///
/// <see cref="BusinessHoursOptions"/> já declarava <c>ValidateOnStart</c>, mas sem nenhum
/// validador registrado — ou seja, não validava nada. O que passava calado:
///
/// <list type="bullet">
/// <item>fuso inexistente: a resolução só falha quando alguém abre um chamado, e o erro
/// é um <c>TimeZoneNotFoundException</c> no meio do cálculo de SLA;</item>
/// <item>fim do expediente antes do início: todo prazo vira negativo, sem exceção nenhuma
/// para acusar — o chamado nasce vencido e o dashboard mente;</item>
/// <item>nenhum dia útil: o cálculo procura o próximo dia de atendimento para sempre.</item>
/// </list>
///
/// Os três são silenciosos em produção e invisíveis em desenvolvimento, onde o padrão
/// está correto. Falhar na subida é o único momento em que a mensagem chega a quem
/// configurou.
/// </summary>
public class BusinessHoursOptionsValidator : IValidateOptions<BusinessHoursOptions>
{
    public ValidateOptionsResult Validate(string? name, BusinessHoursOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TimeZone))
        {
            failures.Add("BusinessHours:TimeZone não pode ser vazio.");
        }
        else if (!TryResolve(options.TimeZone))
        {
            // A imagem da API instala tzdata justamente para que este identificador exista.
            failures.Add(
                $"BusinessHours:TimeZone '{options.TimeZone}' não existe neste sistema. " +
                "Use um identificador IANA, como America/Sao_Paulo.");
        }

        if (options.End <= options.Start)
        {
            failures.Add(
                $"BusinessHours:End ({options.End}) precisa ser depois de " +
                $"BusinessHours:Start ({options.Start}).");
        }

        if (options.WorkingDays.Length == 0)
        {
            failures.Add("BusinessHours:WorkingDays precisa ter ao menos um dia.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool TryResolve(string timeZone)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone);

            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
