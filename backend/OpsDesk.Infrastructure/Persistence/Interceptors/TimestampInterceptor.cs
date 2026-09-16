using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Common;

namespace OpsDesk.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Preenche <c>CreatedAt</c> e <c>UpdatedAt</c>. Ficar a cargo dos serviços significaria
/// que a primeira distração deixa uma entidade sem data, e em UTC ninguém confere a olho.
/// </summary>
public class TimestampInterceptor(IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IHasCreatedAt>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<IHasUpdatedAt>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
