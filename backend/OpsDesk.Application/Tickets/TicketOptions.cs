using System.ComponentModel.DataAnnotations;

namespace OpsDesk.Application.Tickets;

/// <summary>Regras de chamado que dependem de configuração.</summary>
public class TicketOptions
{
    public const string SectionName = "Tickets";

    /// <summary>
    /// Por quantos dias corridos, contados do fechamento, um chamado fechado ainda pode
    /// ser reaberto (README, seção 5.9). Depois disso, "o problema voltou" é chamado novo.
    ///
    /// Sem janela, um "obrigado!" respondido meses depois ressuscitaria um chamado antigo
    /// — e, na versão 2.0, qualquer resposta ao e-mail faria o mesmo. Zero desliga a
    /// reabertura de chamado fechado; resolvido, que ainda não foi fechado, reabre sempre.
    /// </summary>
    [Range(0, 365)]
    public int ReopenWindowDays { get; set; } = 7;
}
