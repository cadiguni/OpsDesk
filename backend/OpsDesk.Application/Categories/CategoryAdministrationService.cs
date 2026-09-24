using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Common;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Tickets;

namespace OpsDesk.Application.Categories;

/// <summary>
/// Administração do catálogo de categorias pelo gestor: criar, editar, desativar e reativar.
///
/// Não há exclusão. Chamado aponta para a categoria, e o histórico guarda o identificador
/// dela; apagar deixaria os dois sem referência. Desativar tira a categoria dos seletores
/// — abertura e reclassificação já recusam categoria inativa — e os chamados que já a usam
/// continuam válidos, exibindo o nome dela normalmente.
///
/// O acesso é restrito a gestor pela política da rota. Diferente da administração de
/// usuários, aqui não há conferência do perfil no banco: o pior que um gestor recém-rebaixado
/// faz com o token ainda válido é renomear uma categoria, o que se desfaz pela mesma tela.
/// </summary>
public class CategoryAdministrationService(
    IOpsDeskDbContext db,
    ILogger<CategoryAdministrationService> logger)
{
    public Task<PagedResult<ManagedCategory>> ListAsync(
        ManagedCategoryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = db.Categories.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();

            query = query.Where(c => c.Name.ToLower().Contains(term));
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(c => c.IsActive == isActive);
        }

        return Project(query.OrderBy(c => c.Name)).ToPagedResultAsync(filter.Page, cancellationToken);
    }

    public async Task<SaveCategoryResult> CreateAsync(
        SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();

        if (await NameTakenAsync(name, exceptId: null, cancellationToken))
        {
            return new SaveCategoryResult.NameTaken();
        }

        var category = new Category { Name = name, Description = Clean(request.Description) };

        db.Categories.Add(category);

        if (!await TrySaveAsync(cancellationToken))
        {
            return new SaveCategoryResult.NameTaken();
        }

        logger.LogInformation("Categoria {CategoryId} criada: {Name}.", category.Id, name);

        return await SavedAsync(category.Id, cancellationToken);
    }

    /// <summary>
    /// Renomeia e troca a descrição. Seguro para o histórico: ele guarda o identificador da
    /// categoria, não o nome, então a troca não reescreve o passado de chamado nenhum.
    /// </summary>
    public async Task<SaveCategoryResult> UpdateAsync(
        Guid categoryId, SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return new SaveCategoryResult.CategoryNotFound();
        }

        var name = request.Name.Trim();

        if (await NameTakenAsync(name, exceptId: category.Id, cancellationToken))
        {
            return new SaveCategoryResult.NameTaken();
        }

        var previousName = category.Name;
        category.Name = name;
        category.Description = Clean(request.Description);

        if (!await TrySaveAsync(cancellationToken))
        {
            return new SaveCategoryResult.NameTaken();
        }

        if (previousName != name)
        {
            logger.LogInformation(
                "Categoria {CategoryId} renomeada de {PreviousName} para {Name}.",
                category.Id, previousName, name);
        }

        return await SavedAsync(category.Id, cancellationToken);
    }

    public async Task<SaveCategoryResult> ChangeActivationAsync(
        Guid categoryId, ChangeCategoryActivationRequest request, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return new SaveCategoryResult.CategoryNotFound();
        }

        if (category.IsActive == request.IsActive)
        {
            return await SavedAsync(category.Id, cancellationToken);
        }

        if (!request.IsActive
            && !await db.Categories.AnyAsync(c => c.IsActive && c.Id != category.Id, cancellationToken))
        {
            return new SaveCategoryResult.LastActiveCategory();
        }

        category.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Categoria {CategoryId} {Action}.", category.Id, request.IsActive ? "reativada" : "desativada");

        return await SavedAsync(category.Id, cancellationToken);
    }

    /// <summary>
    /// Nome já usado, sem diferenciar maiúsculas. O índice único do banco diferencia, então
    /// é esta verificação que impede "VPN" e "vpn"; o índice fica como rede para a corrida
    /// entre duas gravações do mesmo nome exato.
    /// </summary>
    private Task<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        return db.Categories.AnyAsync(
            c => c.Name.ToLower() == lowered && c.Id != exceptId, cancellationToken);
    }

    /// <summary>Grava; colisão no índice único vira "nome em uso", e não 500.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task<SaveCategoryResult> SavedAsync(Guid categoryId, CancellationToken cancellationToken) =>
        new SaveCategoryResult.Saved(
            await Project(db.Categories.AsNoTracking().Where(c => c.Id == categoryId))
                .SingleAsync(cancellationToken));

    private static string? Clean(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static IQueryable<ManagedCategory> Project(IQueryable<Category> categories) =>
        categories.Select(c => new ManagedCategory(
            c.Id,
            c.Name,
            c.Description,
            c.IsActive,
            c.CreatedAt,
            c.Tickets.Count(t => !TicketStatusMachine.Terminal.Contains(t.Status)),
            c.Tickets.Count));
}
