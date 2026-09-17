using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Attachments;
using OpsDesk.Application.Authorization;
using OpsDesk.Application.Common;
using OpsDesk.Application.Sla;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Domain.Tickets;

namespace OpsDesk.Application.Tickets;

/// <summary>
/// Abertura, listagem e detalhe do chamado.
///
/// Toda leitura começa por <c>VisibleTo</c> (invariante 1 do CLAUDE.md): o filtro entra no
/// <see cref="IQueryable{T}"/> antes de qualquer outro <c>Where</c> e antes da projeção,
/// então não existe caminho em que um filtro esquecido exponha chamado de terceiro.
/// </summary>
public class TicketService(
    IOpsDeskDbContext db, SlaClock sla, IClock clock, AttachmentService attachments)
{
    /// <param name="author">Quem está autenticado — nem sempre é o solicitante do chamado.</param>
    public async Task<CreateTicketResult> CreateAsync(
        CreateTicketRequest request, TicketViewer author, CancellationToken cancellationToken = default)
    {
        var categoryExists = await db.Categories
            .AnyAsync(c => c.Id == request.CategoryId && c.IsActive, cancellationToken);

        if (!categoryExists)
        {
            return new CreateTicketResult.CategoryNotFound();
        }

        // Abertura em nome de terceiro: o técnico atende alguém por telefone ou presencial
        // e registra o chamado no nome de quem pediu, para que o chamado apareça na lista
        // dessa pessoa e as respostas cheguem a ela. Quem abriu de fato fica no histórico,
        // gravado pelo interceptor — o campo Requester diz de quem é o problema, não quem
        // digitou.
        var requesterId = request.RequesterId ?? author.UserId;

        if (requesterId != author.UserId)
        {
            if (!author.IsStaff)
            {
                return new CreateTicketResult.RequesterNotAllowed();
            }

            var requesterExists = await db.Users
                .AnyAsync(u => u.Id == requesterId && u.IsActive, cancellationToken);

            if (!requesterExists)
            {
                return new CreateTicketResult.RequesterNotFound();
            }
        }

        var policy = await db.SlaPolicies
            .SingleOrDefaultAsync(p => p.Priority == request.Priority && p.IsActive, cancellationToken);

        if (policy is null)
        {
            // Sem política ativa não há prazo, e chamado sem prazo não entra em indicador
            // nenhum. Falhar alto é melhor que gravar um chamado silenciosamente invisível
            // para o SLA.
            return new CreateTicketResult.SlaPolicyMissing(request.Priority);
        }

        var now = clock.UtcNow;
        var deadlines = sla.Calculate(now, policy);

        var ticket = new Ticket
        {
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            CategoryId = request.CategoryId,
            Priority = request.Priority,
            RequesterId = requesterId,

            // Automáticos, README seção 9. O status inicial é sempre Aberto, e a origem
            // é sempre Portal na versão 1.
            Status = TicketStatus.Open,
            Source = TicketSource.Portal,
            SlaResponseDueAt = deadlines.ResponseDueAt,
            SlaResolutionDueAt = deadlines.ResolutionDueAt,

            // Mesmo instante que originou os prazos.
            //
            // Deixar o interceptor estampar a data — ele só preenche o que está vazio —
            // usaria o relógio de novo, alguns microssegundos depois, e o chamado nasceria
            // com prazo de resposta anterior a "criado mais uma hora útil". A diferença é
            // irrelevante na tela e nada nela avisa que existe, mas é o tipo de
            // inconsistência que estraga conta de indicador.
            CreatedAt = now
        };

        db.Tickets.Add(ticket);

        // Anexos da abertura são sempre públicos: é o print que o solicitante mandou junto
        // com o problema. O identificador do chamado já existe antes do SaveChanges, então
        // o vínculo entra na mesma transação.
        if (!await attachments.TryBindAsync(
                request.AttachmentIds ?? [], ticket.Id, null, false, author, cancellationToken))
        {
            return new CreateTicketResult.AttachmentsInvalid();
        }

        // O código vem da sequence do banco e volta preenchido nesta mesma operação; o
        // histórico de criação é gravado pelo interceptor. Nada disso é feito aqui.
        await db.SaveChangesAsync(cancellationToken);

        var detail = await GetAsync(ticket.Id, author, cancellationToken);

        return new CreateTicketResult.Created(detail!);
    }

    public Task<PagedResult<TicketListItem>> ListAsync(
        TicketFilter filter, TicketViewer viewer, CancellationToken cancellationToken = default)
    {
        var query = db.Tickets
            .AsNoTracking()
            .VisibleTo(viewer)
            .ApplyFilter(filter, clock.UtcNow)
            .ApplySort(filter.Sort)
            .Select(t => new TicketListItem(
                t.Id,
                t.Code,
                t.Title,
                t.Status,
                t.Priority,
                t.Category.Name,
                t.Requester.Name,
                t.AssignedTechnician == null ? null : t.AssignedTechnician.Name,
                t.CreatedAt,
                t.SlaResponseDueAt,
                t.SlaResolutionDueAt,
                t.FirstRespondedAt,
                t.ResolvedAt));

        return query.ToPagedResultAsync(filter.Page, cancellationToken);
    }

    /// <summary>
    /// Detalhe do chamado, ou <c>null</c> quando o usuário não pode vê-lo.
    ///
    /// Chamado de terceiro e chamado inexistente devolvem a mesma coisa de propósito:
    /// distinguir "não existe" de "existe mas não é seu" confirmaria a existência do
    /// chamado para quem estivesse testando identificadores.
    /// </summary>
    public async Task<TicketDetail?> GetAsync(
        Guid id, TicketViewer viewer, CancellationToken cancellationToken = default)
    {
        var row = await db.Tickets
            .AsNoTracking()
            .VisibleTo(viewer)
            .Where(t => t.Id == id)
            .Select(t => new TicketDetailRow(
                t.Id,
                t.Code,
                t.Title,
                t.Description,
                t.Status,
                t.Priority,
                t.Source,
                t.CategoryId,
                t.Category.Name,
                t.RequesterId,
                t.Requester.Name,
                t.AssignedTechnicianId,
                t.AssignedTechnician == null ? null : t.AssignedTechnician.Name,
                t.CreatedAt,
                t.UpdatedAt,
                t.SlaResponseDueAt,
                t.SlaResolutionDueAt,
                t.FirstRespondedAt,
                t.ResolvedAt,
                t.ClosedAt,
                t.SlaPausedAt != null,
                t.SlaPausedBusinessMinutes))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : ToDetail(row, viewer);
    }

    /// <summary>
    /// Histórico do chamado, do mais antigo para o mais recente.
    ///
    /// Segue a visibilidade do chamado: lista vazia quando o chamado não é visível, o que é
    /// indistinguível de um chamado sem histórico.
    /// </summary>
    public Task<List<TicketHistoryItem>> ListHistoryAsync(
        Guid ticketId, TicketViewer viewer, CancellationToken cancellationToken = default) =>
        db.TicketHistory
            .AsNoTracking()
            .Where(h => h.TicketId == ticketId)
            .VisibleTo(viewer)
            .OrderBy(h => h.CreatedAt)
            .Select(h => new TicketHistoryItem(
                h.Id,
                h.Action,
                h.PreviousValue,
                h.NewValue,
                h.ChangedById,
                h.ChangedBy == null ? null : h.ChangedBy.Name,
                h.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task<List<CategoryOption>> ListCategoriesAsync(CancellationToken cancellationToken = default) =>
        db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryOption(c.Id, c.Name, c.Description))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// O chamado como sai do banco.
    ///
    /// Existe porque as transições permitidas não podem ser calculadas em SQL: a máquina de
    /// estados é código, e tentar projetá-la dentro do <c>Select</c> faria o EF Core recusar
    /// a query. Materializamos as colunas e o grafo é consultado aqui.
    /// </summary>
    private record TicketDetailRow(
        Guid Id,
        string Code,
        string Title,
        string Description,
        TicketStatus Status,
        TicketPriority Priority,
        TicketSource Source,
        Guid CategoryId,
        string CategoryName,
        Guid RequesterId,
        string RequesterName,
        Guid? AssignedTechnicianId,
        string? AssignedTechnicianName,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset SlaResponseDueAt,
        DateTimeOffset SlaResolutionDueAt,
        DateTimeOffset? FirstRespondedAt,
        DateTimeOffset? ResolvedAt,
        DateTimeOffset? ClosedAt,
        bool SlaPaused,
        int SlaPausedBusinessMinutes);

    private static TicketDetail ToDetail(TicketDetailRow row, TicketViewer viewer) => new(
        row.Id,
        row.Code,
        row.Title,
        row.Description,
        row.Status,
        row.Priority,
        row.Source,
        row.CategoryId,
        row.CategoryName,
        row.RequesterId,
        row.RequesterName,
        row.AssignedTechnicianId,
        row.AssignedTechnicianName,
        row.CreatedAt,
        row.UpdatedAt,
        row.SlaResponseDueAt,
        row.SlaResolutionDueAt,
        row.FirstRespondedAt,
        row.ResolvedAt,
        row.ClosedAt,
        row.SlaPaused,
        row.SlaPausedBusinessMinutes,

        // Os destinos que este perfil pode escolher. A interface usa isto para montar o
        // seletor de status; a verificação de verdade acontece na mudança de status,
        // contra a mesma máquina de estados.
        [.. TicketStatusMachine
            .AllowedFrom(row.Status)
            .Where(next => TicketStatusMachine.IsAllowedForRole(row.Status, next, viewer.Role))]);
}

public abstract record CreateTicketResult
{
    public sealed record Created(TicketDetail Ticket) : CreateTicketResult;

    public sealed record CategoryNotFound : CreateTicketResult
    {
        public string Message => "Categoria inválida ou inativa.";
    }

    public sealed record SlaPolicyMissing(TicketPriority Priority) : CreateTicketResult
    {
        public string Message => $"Não há política de SLA ativa para a prioridade {Priority}.";
    }

    public sealed record RequesterNotFound : CreateTicketResult
    {
        public string Message => "Solicitante inválido ou inativo.";
    }

    public sealed record RequesterNotAllowed : CreateTicketResult
    {
        public string Message =>
            "Apenas técnicos e gestores abrem chamado em nome de outra pessoa.";
    }

    public sealed record AttachmentsInvalid : CreateTicketResult
    {
        public string Message =>
            "Algum anexo não foi encontrado ou já pertence a outro chamado. O chamado não foi aberto.";
    }
}
