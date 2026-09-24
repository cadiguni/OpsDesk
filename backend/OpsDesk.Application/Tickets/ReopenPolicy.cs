using Microsoft.Extensions.Options;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Tickets;

/// <summary>
/// Janela de reabertura de chamado fechado.
///
/// Fica fora do <c>TicketStatusMachine</c> porque depende do relógio e da configuração, e
/// o grafo é um valor puro. A máquina diz que a aresta Fechado → Em atendimento existe;
/// esta classe diz se ela ainda vale para um chamado específico.
/// </summary>
public class ReopenPolicy(IOptions<TicketOptions> options)
{
    public int WindowDays => options.Value.ReopenWindowDays;

    /// <summary>
    /// Até quando o chamado pode ser reaberto. Nulo quando a pergunta não se aplica —
    /// chamado que não está fechado.
    /// </summary>
    public DateTimeOffset? ReopenableUntil(TicketStatus status, DateTimeOffset? closedAt) =>
        status == TicketStatus.Closed && closedAt is { } closed
            ? closed.AddDays(WindowDays)
            : null;

    /// <summary>
    /// Se o chamado, neste status, ainda pode voltar para atendimento. Resolvido pode
    /// sempre; fechado, só dentro da janela; os demais não estão encerrados.
    /// </summary>
    public bool CanReopen(TicketStatus status, DateTimeOffset? closedAt, DateTimeOffset now) => status switch
    {
        TicketStatus.Resolved => true,
        TicketStatus.Closed => ReopenableUntil(status, closedAt) is { } until && now < until,
        _ => false
    };
}
