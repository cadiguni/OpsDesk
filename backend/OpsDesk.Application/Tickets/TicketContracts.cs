using OpsDesk.Application.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Tickets;

/// <summary>
/// Abertura de chamado (README, seção 9).
///
/// Só o que o solicitante informa. Código, status inicial, datas, solicitante, prazo de
/// SLA e origem são automáticos — e prioridade **não** é inferida do texto: a triagem é
/// da equipe.
/// </summary>
public record CreateTicketRequest(string Title, string Description, Guid CategoryId, TicketPriority Priority);

/// <summary>Resumo do chamado para as listagens.</summary>
public record TicketListItem(
    Guid Id,
    string Code,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    string CategoryName,
    string RequesterName,
    string? AssignedTechnicianName,
    DateTimeOffset CreatedAt,
    DateTimeOffset SlaResponseDueAt,
    DateTimeOffset SlaResolutionDueAt,
    DateTimeOffset? FirstRespondedAt,
    DateTimeOffset? ResolvedAt);

/// <summary>Chamado completo, para a tela de detalhe (README, seção 13.5).</summary>
public record TicketDetail(
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
    int SlaPausedBusinessMinutes,
    IReadOnlyList<TicketStatus> AllowedNextStatuses);

/// <summary>
/// Filtros da listagem. Todos opcionais e todos aplicados em SQL.
///
/// `Search` cobre código e título, que é o que a pessoa tem em mãos quando vem procurar
/// um chamado específico.
/// </summary>
public record TicketFilter(
    TicketStatus[]? Status = null,
    TicketPriority[]? Priority = null,
    Guid? CategoryId = null,
    Guid? AssignedTechnicianId = null,
    bool? Unassigned = null,
    bool? Overdue = null,
    string? Search = null,
    TicketSort Sort = TicketSort.CreatedAtDescending)
{
    /// <summary>Paginação, separada dos filtros para o limite máximo ficar no mesmo lugar.</summary>
    public PageRequest Page { get; init; } = new();
}

public enum TicketSort
{
    CreatedAtDescending,
    CreatedAtAscending,

    /// <summary>Prazo de resolução mais próximo primeiro. A ordem útil para o painel técnico.</summary>
    ResolutionDueAtAscending,

    /// <summary>Crítica primeiro.</summary>
    PriorityDescending
}

/// <summary>Categoria ativa, para os seletores da interface.</summary>
public record CategoryOption(Guid Id, string Name, string? Description);
