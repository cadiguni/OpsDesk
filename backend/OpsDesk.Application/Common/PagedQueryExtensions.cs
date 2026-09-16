using Microsoft.EntityFrameworkCore;

namespace OpsDesk.Application.Common;

public static class PagedQueryExtensions
{
    /// <summary>
    /// Conta e pagina no banco. A projeção precisa acontecer antes, para que o SQL
    /// traga só as colunas usadas.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageRequest page, CancellationToken cancellationToken = default)
    {
        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }
}
