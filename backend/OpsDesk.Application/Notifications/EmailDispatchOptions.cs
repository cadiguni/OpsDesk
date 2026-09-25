using System.ComponentModel.DataAnnotations;

namespace OpsDesk.Application.Notifications;

public class EmailDispatchOptions
{
    public const string SectionName = "EmailDispatch";

    /// <summary>
    /// Intervalo entre rodadas do despachante. Zero desliga — útil em teste, onde envio
    /// periódico é teste que depende de horário, e o despacho é exercitado diretamente.
    /// </summary>
    [Range(0, 3600)]
    public int DispatchIntervalSeconds { get; set; } = 30;

    /// <summary>Quantos e-mails por rodada. Limita o tempo de uma rodada com o provedor lento.</summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 20;
}
