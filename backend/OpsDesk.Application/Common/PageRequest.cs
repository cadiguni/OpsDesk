namespace OpsDesk.Application.Common;

/// <summary>
/// Paginação de listagem. O limite máximo é do servidor, não do cliente: pedido de
/// página maior é reduzido em silêncio, nunca honrado.
/// </summary>
public readonly record struct PageRequest
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public PageRequest(int? page = null, int? pageSize = null)
    {
        Page = page is > 0 ? page.Value : 1;
        PageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;
}
