using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Grava a trilha de auditoria de usuário (<see cref="UserAuditEntry"/>) a partir do que
/// mudou no <see cref="User"/>: criação, perfil, cargo, ativação e senha.
///
/// Pelo mesmo motivo do <see cref="TicketHistoryInterceptor"/>: auditoria que depende de
/// cada serviço lembrar de gravar falha no primeiro caminho novo. E, como lá, a trilha é
/// append-only.
/// </summary>
public class UserAuditInterceptor(ICurrentUser currentUser, IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Record(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        if (context.ChangeTracker.Entries<UserAuditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "A trilha de auditoria de usuário é append-only: alteração e exclusão não são permitidas.");
        }

        var actor = currentUser.UserId;
        var now = clock.UtcNow;
        var entries = new List<UserAuditEntry>();

        foreach (var entry in context.ChangeTracker.Entries<User>().ToList())
        {
            var user = entry.Entity;

            switch (entry.State)
            {
                case EntityState.Added:
                    entries.Add(New(user.Id, UserAuditAction.Created, null, user.Role.ToString(), actor, now));
                    break;

                case EntityState.Modified:
                    entries.AddRange(Describe(context, entry, actor, now));
                    break;
            }

            // O recado vale para esta gravação e para nenhuma outra.
            user.PasswordChangeReason = null;
        }

        if (entries.Count > 0)
        {
            context.Set<UserAuditEntry>().AddRange(entries);
        }
    }

    private static IEnumerable<UserAuditEntry> Describe(
        DbContext context, EntityEntry<User> entry, Guid? actor, DateTimeOffset now)
    {
        var user = entry.Entity;

        var role = entry.Property(u => u.Role);
        if (role.IsModified && role.OriginalValue != role.CurrentValue)
        {
            yield return New(user.Id, UserAuditAction.RoleChanged,
                role.OriginalValue.ToString(), role.CurrentValue.ToString(), actor, now);
        }

        var active = entry.Property(u => u.IsActive);
        if (active.IsModified && active.OriginalValue != active.CurrentValue)
        {
            yield return New(user.Id,
                active.CurrentValue ? UserAuditAction.Reactivated : UserAuditAction.Deactivated,
                null, null, actor, now);
        }

        var jobTitle = entry.Property(u => u.JobTitleId);
        if (jobTitle.IsModified && jobTitle.OriginalValue != jobTitle.CurrentValue)
        {
            yield return New(user.Id, UserAuditAction.JobTitleChanged,
                JobTitleName(context, jobTitle.OriginalValue), JobTitleName(context, jobTitle.CurrentValue), actor, now);
        }

        var entra = entry.Property(u => u.EntraObjectId);
        if (entra.IsModified && entra.CurrentValue is { } objectId && entra.OriginalValue != objectId)
        {
            yield return New(user.Id, UserAuditAction.MicrosoftAccountLinked, entra.OriginalValue, objectId, actor, now);
        }

        // Só com motivo declarado: o rehash do login troca o hash sem trocar a senha.
        var password = entry.Property(u => u.PasswordHash);
        if (password.IsModified && user.PasswordChangeReason is { } reason)
        {
            yield return New(user.Id, reason, null, null, actor, now);
        }
    }

    /// <summary>
    /// Nome do cargo, para a trilha. O serviço deixa os dois cargos rastreados antes de
    /// gravar, e o <c>Find</c> os acha sem consulta; fora disso, ele consulta.
    /// </summary>
    private static string? JobTitleName(DbContext context, Guid? jobTitleId) =>
        jobTitleId is { } id ? context.Set<JobTitle>().Find(id)?.Name : null;

    private static UserAuditEntry New(
        Guid userId, UserAuditAction action, string? previous, string? current, Guid? actor, DateTimeOffset now) => new()
        {
            UserId = userId,
            Action = action,
            PreviousValue = previous,
            NewValue = current,
            ChangedById = actor,
            CreatedAt = now
        };
}
