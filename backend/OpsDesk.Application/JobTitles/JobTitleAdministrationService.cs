using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Common;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Application.JobTitles;

/// <summary>
/// Catálogo de cargos, administrado pelo administrador do portal: criar, renomear,
/// desativar e reativar.
///
/// Segue o desenho das categorias. Não há exclusão: quem tem o cargo continua com ele, e
/// a trilha de auditoria guarda o nome. Desativar só tira o cargo do seletor. Como cargo
/// não dá permissão nenhuma, não há conferência do perfil no banco além da política da rota.
/// </summary>
public class JobTitleAdministrationService(
    IOpsDeskDbContext db,
    ILogger<JobTitleAdministrationService> logger)
{
    public Task<PagedResult<ManagedJobTitle>> ListAsync(
        ManagedJobTitleFilter filter, CancellationToken cancellationToken = default)
    {
        var query = db.JobTitles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();

            query = query.Where(j => j.Name.ToLower().Contains(term));
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(j => j.IsActive == isActive);
        }

        return Project(query.OrderBy(j => j.Name)).ToPagedResultAsync(filter.Page, cancellationToken);
    }

    /// <summary>Cargos ativos, sem paginação: o catálogo é de dezenas, não de milhares.</summary>
    public Task<List<JobTitleOption>> OptionsAsync(CancellationToken cancellationToken = default) =>
        db.JobTitles
            .AsNoTracking()
            .Where(j => j.IsActive)
            .OrderBy(j => j.Name)
            .Select(j => new JobTitleOption(j.Id, j.Name))
            .ToListAsync(cancellationToken);

    public async Task<SaveJobTitleResult> CreateAsync(
        SaveJobTitleRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();

        if (await NameTakenAsync(name, exceptId: null, cancellationToken))
        {
            return new SaveJobTitleResult.NameTaken();
        }

        var jobTitle = new JobTitle { Name = name };

        db.JobTitles.Add(jobTitle);

        if (!await TrySaveAsync(cancellationToken))
        {
            return new SaveJobTitleResult.NameTaken();
        }

        logger.LogInformation("Cargo {JobTitleId} criado: {Name}.", jobTitle.Id, name);

        return await SavedAsync(jobTitle.Id, cancellationToken);
    }

    public async Task<SaveJobTitleResult> UpdateAsync(
        Guid jobTitleId, SaveJobTitleRequest request, CancellationToken cancellationToken = default)
    {
        var jobTitle = await db.JobTitles.SingleOrDefaultAsync(j => j.Id == jobTitleId, cancellationToken);

        if (jobTitle is null)
        {
            return new SaveJobTitleResult.JobTitleNotFound();
        }

        var name = request.Name.Trim();

        if (await NameTakenAsync(name, exceptId: jobTitle.Id, cancellationToken))
        {
            return new SaveJobTitleResult.NameTaken();
        }

        var previousName = jobTitle.Name;
        jobTitle.Name = name;

        if (!await TrySaveAsync(cancellationToken))
        {
            return new SaveJobTitleResult.NameTaken();
        }

        if (previousName != name)
        {
            logger.LogInformation(
                "Cargo {JobTitleId} renomeado de {PreviousName} para {Name}.", jobTitle.Id, previousName, name);
        }

        return await SavedAsync(jobTitle.Id, cancellationToken);
    }

    public async Task<SaveJobTitleResult> ChangeActivationAsync(
        Guid jobTitleId, ChangeJobTitleActivationRequest request, CancellationToken cancellationToken = default)
    {
        var jobTitle = await db.JobTitles.SingleOrDefaultAsync(j => j.Id == jobTitleId, cancellationToken);

        if (jobTitle is null)
        {
            return new SaveJobTitleResult.JobTitleNotFound();
        }

        if (jobTitle.IsActive != request.IsActive)
        {
            jobTitle.IsActive = request.IsActive;

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Cargo {JobTitleId} {Action}.", jobTitle.Id, request.IsActive ? "reativado" : "desativado");
        }

        return await SavedAsync(jobTitle.Id, cancellationToken);
    }

    /// <summary>Nome já usado, sem diferenciar maiúsculas; o índice único fica como rede para a corrida.</summary>
    private Task<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        return db.JobTitles.AnyAsync(j => j.Name.ToLower() == lowered && j.Id != exceptId, cancellationToken);
    }

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

    private async Task<SaveJobTitleResult> SavedAsync(Guid jobTitleId, CancellationToken cancellationToken) =>
        new SaveJobTitleResult.Saved(
            await Project(db.JobTitles.AsNoTracking().Where(j => j.Id == jobTitleId)).SingleAsync(cancellationToken));

    private static IQueryable<ManagedJobTitle> Project(IQueryable<JobTitle> jobTitles) =>
        jobTitles.Select(j => new ManagedJobTitle(j.Id, j.Name, j.IsActive, j.CreatedAt, j.Users.Count));
}
