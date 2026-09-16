using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Grava <see cref="TicketHistory"/> a partir do que mudou no <see cref="Ticket"/>
/// (decisão 4.2 de docs/arquitetura.md, invariante 3 do CLAUDE.md).
///
/// O histórico não é responsabilidade dos serviços de propósito: auditoria que depende
/// de alguém lembrar de chamar o Add funciona até a primeira distração, e o requisito
/// do README é absoluto.
///
/// O interceptor também recusa alteração e exclusão de histórico: append-only não é
/// convenção, é invariante.
/// </summary>
public class TicketHistoryInterceptor(ICurrentUser currentUser, IClock clock) : SaveChangesInterceptor
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

        GuardAppendOnly(context);

        var now = clock.UtcNow;
        var author = currentUser.UserId;
        var entries = new List<TicketHistory>();

        foreach (var entry in context.ChangeTracker.Entries<Ticket>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entries.Add(Entry(entry.Entity.Id, TicketHistoryAction.Created,
                        null, entry.Entity.Status.ToString(), author, now));
                    break;

                case EntityState.Modified:
                    entries.AddRange(DescribeChanges(entry, author, now));
                    break;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<TicketComment>().ToList())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            var action = entry.Entity.IsInternal
                ? TicketHistoryAction.InternalCommentAdded
                : TicketHistoryAction.CommentAdded;

            // O conteúdo do comentário não entra no histórico: o histórico é a trilha de
            // quem mexeu em quê, e duplicar o texto espalharia comentário interno por uma
            // tabela com outra regra de visibilidade.
            entries.Add(Entry(entry.Entity.TicketId, action, null, null, author, now));
        }

        if (entries.Count > 0)
        {
            context.Set<TicketHistory>().AddRange(entries);
        }
    }

    private static IEnumerable<TicketHistory> DescribeChanges(
        EntityEntry<Ticket> entry, Guid? author, DateTimeOffset now)
    {
        var ticketId = entry.Entity.Id;

        if (Changed(entry, t => t.Status, out var previousStatus, out var newStatus))
        {
            // Resolvido, fechado e cancelado ganham ação própria porque o README lista
            // esses três eventos separadamente. Um registro por mudança, nunca dois.
            var action = newStatus switch
            {
                TicketStatus.Resolved => TicketHistoryAction.Resolved,
                TicketStatus.Closed => TicketHistoryAction.Closed,
                TicketStatus.Cancelled => TicketHistoryAction.Cancelled,
                _ => TicketHistoryAction.StatusChanged
            };

            yield return Entry(ticketId, action,
                previousStatus.ToString(), newStatus.ToString(), author, now);
        }

        if (Changed(entry, t => t.Priority, out var previousPriority, out var newPriority))
        {
            yield return Entry(ticketId, TicketHistoryAction.PriorityChanged,
                previousPriority.ToString(), newPriority.ToString(), author, now);
        }

        if (Changed(entry, t => t.CategoryId, out var previousCategory, out var newCategory))
        {
            // Guardamos o id, não o nome: nome de categoria pode ser editado depois e o
            // histórico passaria a mentir. A leitura resolve o id para nome.
            yield return Entry(ticketId, TicketHistoryAction.CategoryChanged,
                previousCategory.ToString(), newCategory.ToString(), author, now);
        }

        if (Changed(entry, t => t.AssignedTechnicianId, out var previousTech, out var newTech))
        {
            var action = newTech is null
                ? TicketHistoryAction.TechnicianUnassigned
                : TicketHistoryAction.TechnicianAssigned;

            yield return Entry(ticketId, action,
                previousTech?.ToString(), newTech?.ToString(), author, now);
        }
    }

    private static bool Changed<TProperty>(
        EntityEntry<Ticket> entry,
        Expression<Func<Ticket, TProperty>> selector,
        out TProperty previous,
        out TProperty current)
    {
        var property = entry.Property(selector);

        previous = property.OriginalValue;
        current = property.CurrentValue;

        return property.IsModified && !Equals(previous, current);
    }

    private static TicketHistory Entry(
        Guid ticketId,
        TicketHistoryAction action,
        string? previousValue,
        string? newValue,
        Guid? author,
        DateTimeOffset now) => new()
        {
            TicketId = ticketId,
            Action = action,
            PreviousValue = previousValue,
            NewValue = newValue,
            ChangedById = author,
            CreatedAt = now
        };

    /// <summary>
    /// Invariante 3 do CLAUDE.md. Barrar aqui, e não por revisão de código, é o que faz a
    /// trilha de auditoria valer alguma coisa.
    /// </summary>
    private static void GuardAppendOnly(DbContext context)
    {
        var tampered = context.ChangeTracker
            .Entries<TicketHistory>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (tampered)
        {
            throw new InvalidOperationException(
                "TicketHistory é append-only: alteração e exclusão de histórico não são permitidas.");
        }
    }
}
