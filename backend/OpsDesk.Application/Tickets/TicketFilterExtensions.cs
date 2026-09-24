using OpsDesk.Domain.Entities;

namespace OpsDesk.Application.Tickets;

/// <summary>
/// Filtros e ordenação da listagem, aplicados em SQL.
///
/// Nada aqui materializa a consulta: cada método devolve o <see cref="IQueryable{T}"/>
/// com mais um predicado. Filtrar em memória seria funcionalmente idêntico e traria a
/// tabela inteira para a aplicação a cada abertura do painel.
/// </summary>
public static class TicketFilterExtensions
{
    public static IQueryable<Ticket> ApplyFilter(
        this IQueryable<Ticket> tickets, TicketFilter filter, DateTimeOffset now)
    {
        if (filter.Status is { Length: > 0 } statuses)
        {
            tickets = tickets.Where(t => statuses.Contains(t.Status));
        }

        if (filter.Priority is { Length: > 0 } priorities)
        {
            tickets = tickets.Where(t => priorities.Contains(t.Priority));
        }

        if (filter.CategoryId is { } categoryId)
        {
            tickets = tickets.Where(t => t.CategoryId == categoryId);
        }

        if (filter.AssignedTechnicianId is { } technicianId)
        {
            tickets = tickets.Where(t => t.AssignedTechnicianId == technicianId);
        }

        if (filter.Unassigned == true)
        {
            tickets = tickets.Where(t => t.AssignedTechnicianId == null);
        }

        if (filter.Overdue == true)
        {
            // Vencido é passar do prazo sem ter atingido o marco. Cancelado fica fora dos
            // indicadores de SLA (README, seção 8.3), e por isso sai daqui também.
            //
            // A regra está duplicada em Ticket.IsResponseOverdue e IsResolutionOverdue,
            // que são as versões em memória usadas para exibir um chamado já carregado.
            // Esta é a versão traduzível para SQL; mexer em uma pede mexer na outra.
            tickets = tickets.Where(t =>
                t.Status != Domain.Enums.TicketStatus.Cancelled &&
                ((t.FirstRespondedAt == null && t.SlaResponseDueAt < now) ||
                 (t.ResolvedAt == null && t.SlaResolutionDueAt < now)));
        }

        // `ToUniversalTime()` não é enfeite. A interface manda o instante com o deslocamento
        // de São Paulo, e o Npgsql recusa parâmetro `timestamptz` com deslocamento
        // diferente de zero — mesmo sendo o instante certo. Sem a conversão, filtrar por
        // período respondia 500.
        if (filter.CreatedFrom is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            tickets = tickets.Where(t => t.CreatedAt >= fromUtc);
        }

        if (filter.CreatedBefore is { } before)
        {
            var beforeUtc = before.ToUniversalTime();
            tickets = tickets.Where(t => t.CreatedAt < beforeUtc);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();

            // Busca por código e título, que é o que a pessoa tem em mãos quando vem
            // procurar um chamado específico.
            //
            // `ToLower()` nos dois lados em vez do `ILIKE` do PostgreSQL: o `ILIKE` viria
            // de `EF.Functions` do provider Npgsql, e referenciá-lo aqui amarraria a
            // camada de aplicação ao banco. Isto traduz para `lower(coluna) LIKE '%...%'`,
            // que é equivalente.
            //
            // Duas limitações conhecidas, aceitáveis no volume de um service desk interno:
            // não usa índice, e não ignora acento. Se a busca crescer, o caminho é um
            // índice funcional sobre `lower(title)` ou busca textual do PostgreSQL.
            tickets = tickets.Where(t =>
                t.Code.ToLower().Contains(term) ||
                t.Title.ToLower().Contains(term));
        }

        return tickets;
    }

    public static IQueryable<Ticket> ApplySort(this IQueryable<Ticket> tickets, TicketSort sort) => sort switch
    {
        TicketSort.CreatedAtAscending => tickets.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
        TicketSort.ResolutionDueAtAscending => tickets.OrderBy(t => t.SlaResolutionDueAt).ThenBy(t => t.Id),
        TicketSort.UpdatedAtDescending => tickets.OrderByDescending(t => t.UpdatedAt).ThenBy(t => t.Id),

        // Prioridade é persistida como string, então ordenar pela coluna daria ordem
        // alfabética — Critical, High, Low, Medium — que não é ordem de urgência. O CASE
        // traduz o enum para peso numérico dentro do SQL.
        TicketSort.PriorityDescending => tickets
            .OrderByDescending(t => t.Priority == Domain.Enums.TicketPriority.Critical ? 4
                : t.Priority == Domain.Enums.TicketPriority.High ? 3
                : t.Priority == Domain.Enums.TicketPriority.Medium ? 2
                : 1)
            .ThenBy(t => t.SlaResolutionDueAt)
            .ThenBy(t => t.Id),

        // A ordenação precisa ser total: sem o desempate por Id, duas linhas com o mesmo
        // CreatedAt podem trocar de lugar entre páginas e um chamado aparecer duas vezes
        // ou nenhuma.
        _ => tickets.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
    };
}
