using Microsoft.EntityFrameworkCore;
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
                Domain.Tickets.TicketSla.MeasuredSources.Contains(t.Source) &&
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
            // Busca por código, título e descrição.
            //
            // `EF.Functions.Like` sobre `lower(coluna)`, e não `Contains` nem o `ILIKE` do
            // Npgsql. O `ILIKE` amarraria a camada de aplicação ao provider; o `Contains`
            // vira `strpos`, que nenhum índice acelera. Já `lower(coluna) LIKE '%termo%'`
            // é exatamente a expressão dos índices de trigramas (pg_trgm) da migration
            // AddTicketSearchTrigramIndexes — mudar a forma da consulta aqui sem mudar o
            // índice lá volta a varrer a tabela, e há teste fixando os dois lados.
            //
            // O termo tem % e _ escapados: buscar "50%" procura o texto "50%", e não
            // "tudo que começa com 50".
            var pattern = $"%{EscapeLike(filter.Search.Trim().ToLower())}%";

            tickets = tickets.Where(t =>
                EF.Functions.Like(t.Code.ToLower(), pattern, LikeEscape) ||
                EF.Functions.Like(t.Title.ToLower(), pattern, LikeEscape) ||
                EF.Functions.Like(t.Description.ToLower(), pattern, LikeEscape));
        }

        return tickets;
    }

    private const string LikeEscape = "\\";

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

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
