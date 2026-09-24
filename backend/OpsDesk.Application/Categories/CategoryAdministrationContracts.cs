using OpsDesk.Application.Common;

namespace OpsDesk.Application.Categories;

/// <summary>Filtro da listagem de administração. Nulo quer dizer "qualquer".</summary>
public record ManagedCategoryFilter(string? Search, bool? IsActive)
{
    public PageRequest Page { get; init; } = new();
}

/// <summary>
/// Categoria vista pelo gestor. As contagens estão aqui para a tela mostrar o peso de
/// desativar ou renomear antes do clique.
/// </summary>
public record ManagedCategory(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    int OpenTickets,
    int TotalTickets);

/// <summary>Criação e edição usam o mesmo corpo: nome e descrição, os dois de uma vez.</summary>
public record SaveCategoryRequest(string Name, string? Description);

public record ChangeCategoryActivationRequest(bool IsActive);

public abstract record SaveCategoryResult
{
    public sealed record Saved(ManagedCategory Category) : SaveCategoryResult;

    public sealed record CategoryNotFound : SaveCategoryResult;

    /// <summary>
    /// Já existe categoria com esse nome, ativa ou não, sem diferenciar maiúsculas.
    ///
    /// Desativada conta de propósito: "VPN" e "vpn" no mesmo catálogo confundem o
    /// dashboard, que agrupa por nome, e reativar uma delas depois daria duas iguais.
    /// </summary>
    public sealed record NameTaken : SaveCategoryResult
    {
        public string Message => "Já existe uma categoria com esse nome. Reative-a ou escolha outro nome.";
    }

    /// <summary>
    /// Desativar a última categoria ativa. Sem nenhuma, ninguém consegue abrir chamado e o
    /// <c>/ready</c> reprova a instalação — o que tira a API do balanceador.
    /// </summary>
    public sealed record LastActiveCategory : SaveCategoryResult
    {
        public string Message => "Esta é a única categoria ativa. Crie ou reative outra antes de desativá-la.";
    }
}
