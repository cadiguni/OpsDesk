using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Tickets;

/// <summary>
/// Quais chamados entram nos indicadores de SLA.
///
/// Só os que nasceram aqui, pelo portal ou pela caixa de e-mail. Chamado importado de
/// outra ferramenta (CSV, Freshdesk, Freshservice) foi atendido quando o prazo do OpsDesk
/// não existia: medi-lo encheria o dashboard de "vencidos" que ninguém poderia ter evitado,
/// e dispararia alerta sobre chamado de anos atrás. Cancelado também fica fora, pela regra
/// de sempre (README, seção 8.3) — essa é aplicada à parte, pelo status.
/// </summary>
public static class TicketSla
{
    public static readonly TicketSource[] MeasuredSources = [TicketSource.Portal, TicketSource.Email];

    public static bool IsMeasured(TicketSource source) => MeasuredSources.Contains(source);
}
