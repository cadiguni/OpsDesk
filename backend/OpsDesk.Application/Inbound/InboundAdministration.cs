using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Authorization;
using OpsDesk.Application.Common;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Inbound;

// ----- Caixas monitoradas -----

public record EmailAccountItem(
    Guid Id,
    string Name,
    string Address,
    Guid? DefaultCategoryId,
    TicketPriority DefaultPriority,
    bool InternalDomainsOnly,
    bool IsActive,
    DateTimeOffset? LastSyncAt,
    string? LastError);

public record SaveEmailAccountRequest(
    string Name, string Address, Guid? DefaultCategoryId, TicketPriority DefaultPriority, bool InternalDomainsOnly, bool IsActive);

public class SaveEmailAccountRequestValidator : AbstractValidator<SaveEmailAccountRequest>
{
    public SaveEmailAccountRequestValidator()
    {
        RuleFor(r => r.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Informe um nome.").MaximumLength(100);
        RuleFor(r => r.Address).NotEmpty().WithMessage("Informe o endereço da caixa.").EmailAddress().WithMessage("Endereço inválido.").MaximumLength(320);
        RuleFor(r => r.DefaultPriority).IsInEnum().WithMessage("Prioridade inválida.");
    }
}

/// <summary>Caixas monitoradas, pelo gestor (Configurações → E-mail).</summary>
public class EmailAccountService(IOpsDeskDbContext db)
{
    public Task<List<EmailAccountItem>> ListAsync(CancellationToken cancellationToken = default) =>
        db.EmailAccounts.AsNoTracking().OrderBy(a => a.Name).Select(Projection).ToListAsync(cancellationToken);

    /// <returns>Nulo quando o endereço já é de outra caixa.</returns>
    public async Task<EmailAccountItem?> SaveAsync(Guid? id, SaveEmailAccountRequest request, CancellationToken cancellationToken = default)
    {
        var address = request.Address.Trim().ToLowerInvariant();

        if (await db.EmailAccounts.AnyAsync(a => a.Address == address && a.Id != id, cancellationToken))
        {
            return null;
        }

        var account = id is { } existing
            ? await db.EmailAccounts.SingleOrDefaultAsync(a => a.Id == existing, cancellationToken)
            : null;

        if (id is not null && account is null)
        {
            throw new KeyNotFoundException();
        }

        if (account is null)
        {
            account = new EmailAccount { Name = request.Name.Trim(), Address = address };
            db.EmailAccounts.Add(account);
        }

        account.Name = request.Name.Trim();
        account.Address = address;
        account.DefaultCategoryId = request.DefaultCategoryId;
        account.DefaultPriority = request.DefaultPriority;
        account.InternalDomainsOnly = request.InternalDomainsOnly;
        account.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return await db.EmailAccounts.AsNoTracking().Where(a => a.Id == account.Id).Select(Projection).SingleAsync(cancellationToken);
    }

    private static readonly System.Linq.Expressions.Expression<Func<EmailAccount, EmailAccountItem>> Projection = a =>
        new EmailAccountItem(a.Id, a.Name, a.Address, a.DefaultCategoryId, a.DefaultPriority, a.InternalDomainsOnly, a.IsActive, a.LastSyncAt, a.LastError);
}

// ----- E-mails recebidos e caixa de SPAM -----

public record InboundEmailItem(
    Guid Id,
    string AccountName,
    string FromName,
    string FromAddress,
    string Subject,
    DateTimeOffset ReceivedAt,
    InboundEmailStatus Status,
    int SpamScore,
    string? SpamReason,
    Guid? TicketId,
    string? TicketCode,
    string? ProcessingError,
    int Attempts);

public record InboundEmailDetail(InboundEmailItem Email, string BodyText, string? ReviewedByName, DateTimeOffset? ReviewedAt);

public record InboundEmailFilter(InboundEmailStatus? Status, string? Search, DateTimeOffset? From, DateTimeOffset? Before)
{
    public PageRequest Page { get; init; } = new();
}

public enum SpamReviewAction
{
    /// <summary>Não é spam: entrega como resposta ou chamado novo.</summary>
    NotSpam,

    /// <summary>Não é spam, e o remetente passa a ser sempre liberado.</summary>
    NotSpamAndAllowSender,

    /// <summary>É spam, e o remetente passa a ser sempre bloqueado.</summary>
    ConfirmAndBlockSender,

    /// <summary>É spam, e o domínio inteiro passa a ser bloqueado.</summary>
    BlockDomain
}

public record ReviewSpamRequest(SpamReviewAction Action);

public record SpamRuleItem(Guid Id, SpamRuleType Type, string Pattern, SpamRuleAction Action, bool IsActive, string? CreatedByName, DateTimeOffset CreatedAt);

public abstract record SpamReviewResult
{
    public sealed record Reviewed(InboundEmailItem Email) : SpamReviewResult;

    public sealed record NotFound : SpamReviewResult;

    public sealed record NotInSpam : SpamReviewResult
    {
        public string Message => "Esta mensagem não está no SPAM.";
    }
}

/// <summary>
/// E-mails recebidos e caixa de SPAM (docs/integracao-email.md, seção 5). Técnicos e gestores
/// — a rota cuida disso; o solicitante nem sabe que a tela existe.
///
/// Toda ação registra quem revisou e quando, e regra de spam só nasce de gesto explícito:
/// nada é aprendido em silêncio a partir de um clique.
/// </summary>
public class InboundEmailService(IOpsDeskDbContext db, IServiceScopeFactory scopes, IClock clock)
{
    public Task<PagedResult<InboundEmailItem>> ListAsync(InboundEmailFilter filter, CancellationToken cancellationToken = default)
    {
        var query = db.InboundEmails.AsNoTracking();

        if (filter.Status is { } status)
        {
            query = query.Where(e => e.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(e =>
                e.FromAddress.Contains(term) || e.FromName.ToLower().Contains(term) || e.Subject.ToLower().Contains(term)
                || (e.SpamReason != null && e.SpamReason.ToLower().Contains(term)));
        }

        if (filter.From is { } from)
        {
            var utc = from.ToUniversalTime();
            query = query.Where(e => e.ReceivedAt >= utc);
        }

        if (filter.Before is { } before)
        {
            var utc = before.ToUniversalTime();
            query = query.Where(e => e.ReceivedAt < utc);
        }

        return query.OrderByDescending(e => e.ReceivedAt).ThenBy(e => e.Id).Select(ItemProjection).ToPagedResultAsync(filter.Page, cancellationToken);
    }

    public async Task<InboundEmailDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.InboundEmails.AsNoTracking().Where(e => e.Id == id).Select(ItemProjection).SingleOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return null;
        }

        var extra = await db.InboundEmails.AsNoTracking().Where(e => e.Id == id)
            .Select(e => new
            {
                e.BodyText,
                e.ReviewedAt,
                Reviewer = db.Users.Where(u => u.Id == e.ReviewedById).Select(u => u.Name).FirstOrDefault()
            })
            .SingleAsync(cancellationToken);

        return new InboundEmailDetail(item, extra.BodyText, extra.Reviewer, extra.ReviewedAt);
    }

    public async Task<SpamReviewResult> ReviewAsync(
        Guid id, ReviewSpamRequest request, TicketViewer reviewer, CancellationToken cancellationToken = default)
    {
        var email = await db.InboundEmails.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (email is null)
        {
            return new SpamReviewResult.NotFound();
        }

        if (email.Status != InboundEmailStatus.Spam)
        {
            return new SpamReviewResult.NotInSpam();
        }

        var sender = email.FromAddress;
        var domain = sender.Contains('@') ? sender[(sender.LastIndexOf('@') + 1)..] : sender;

        switch (request.Action)
        {
            case SpamReviewAction.NotSpamAndAllowSender:
                AddRule(SpamRuleType.SenderAddress, sender, SpamRuleAction.Allow, reviewer);
                break;
            case SpamReviewAction.ConfirmAndBlockSender:
                AddRule(SpamRuleType.SenderAddress, sender, SpamRuleAction.Block, reviewer);
                break;
            case SpamReviewAction.BlockDomain:
                AddRule(SpamRuleType.SenderDomain, domain, SpamRuleAction.Block, reviewer);
                break;
        }

        email.ReviewedById = reviewer.UserId;
        email.ReviewedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (request.Action is SpamReviewAction.NotSpam or SpamReviewAction.NotSpamAndAllowSender)
        {
            // Entrega num escopo próprio: o processamento age em nome do remetente, e não
            // pode trocar a identidade de quem revisou (ver IActorScope).
            await using var scope = scopes.CreateAsyncScope();
            var scoped = scope.ServiceProvider.GetRequiredService<IOpsDeskDbContext>();
            var processor = scope.ServiceProvider.GetRequiredService<InboundEmailProcessor>();
            var tracked = await scoped.InboundEmails.Include(e => e.EmailAccount).SingleAsync(e => e.Id == id, cancellationToken);

            await processor.DeliverAsync(tracked, cancellationToken);
        }

        return new SpamReviewResult.Reviewed(
            await db.InboundEmails.AsNoTracking().Where(e => e.Id == id).Select(ItemProjection).SingleAsync(cancellationToken));
    }

    /// <summary>Exclusão definitiva — só gestor, pela rota.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.InboundEmails.Where(e => e.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    public Task<List<SpamRuleItem>> ListRulesAsync(CancellationToken cancellationToken = default) =>
        db.SpamRules.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new SpamRuleItem(r.Id, r.Type, r.Pattern, r.Action, r.IsActive, r.CreatedBy == null ? null : r.CreatedBy.Name, r.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> SetRuleActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default) =>
        await db.SpamRules.Where(r => r.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.IsActive, isActive), cancellationToken) > 0;

    private void AddRule(SpamRuleType type, string pattern, SpamRuleAction action, TicketViewer reviewer)
    {
        // A regra contrária ao mesmo padrão sai de cena: liberar um remetente que estava
        // bloqueado não pode deixar as duas regras disputando.
        foreach (var opposite in db.SpamRules.Where(r => r.Type == type && r.Pattern == pattern && r.IsActive))
        {
            opposite.IsActive = false;
        }

        db.SpamRules.Add(new SpamRule
        {
            Type = type,
            Pattern = pattern.ToLowerInvariant(),
            Action = action,
            CreatedById = reviewer.UserId,
            CreatedAt = clock.UtcNow
        });
    }

    private static readonly System.Linq.Expressions.Expression<Func<InboundEmail, InboundEmailItem>> ItemProjection = e =>
        new InboundEmailItem(
            e.Id, e.EmailAccount.Name, e.FromName, e.FromAddress, e.Subject, e.ReceivedAt, e.Status, e.SpamScore,
            e.SpamReason, e.TicketId, e.TicketId == null ? null : e.Ticket!.Code, e.ProcessingError, e.Attempts);
}
