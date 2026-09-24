using OpsDesk.Application.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Tickets;

/// <summary>
/// Abertura de chamado (README, seção 9).
///
/// Só o que o solicitante informa. Código, status inicial, datas, prazo de SLA e origem
/// são automáticos — e prioridade **não** é inferida do texto: a triagem é da equipe.
///
/// <paramref name="RequesterId"/> é o chamado aberto em nome de outra pessoa, e só técnico
/// e gestor podem preenchê-lo. Vazio, o solicitante é quem está autenticado.
///
/// <paramref name="AttachmentIds"/> são anexos já enviados e ainda pendentes, que a
/// abertura vincula ao chamado.
/// </summary>
public record CreateTicketRequest(
    string Title,
    string Description,
    Guid CategoryId,
    TicketPriority Priority,
    Guid? RequesterId = null,
    Guid[]? AttachmentIds = null);

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
    DateTimeOffset? ResolvedAt,
    DateTimeOffset UpdatedAt);

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
    IReadOnlyList<TicketStatus> AllowedNextStatuses,
    // Fechado: até quando uma resposta do solicitante, ou a equipe, ainda o reabre.
    // Nulo nos demais status.
    DateTimeOffset? ReopenableUntil);

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

    /// <summary>
    /// Período de abertura: <see cref="CreatedFrom"/> inclusivo, <see cref="CreatedBefore"/>
    /// exclusivo. São instantes, e não datas: "dia 10" começa à meia-noite de São Paulo, e
    /// quem sabe disso é a interface (invariante 4). Intervalo semiaberto para que dois
    /// períodos vizinhos não contem o mesmo chamado duas vezes.
    /// </summary>
    public DateTimeOffset? CreatedFrom { get; init; }

    public DateTimeOffset? CreatedBefore { get; init; }
}

public enum TicketSort
{
    CreatedAtDescending,
    CreatedAtAscending,

    /// <summary>Prazo de resolução mais próximo primeiro. A ordem útil para o painel técnico.</summary>
    ResolutionDueAtAscending,

    /// <summary>Crítica primeiro.</summary>
    PriorityDescending,

    /// <summary>
    /// Mexido por último primeiro. Comentário conta como mexer: é a resposta do solicitante
    /// que a equipe mais precisa ver subir na fila.
    /// </summary>
    UpdatedAtDescending
}

/// <summary>Categoria ativa, para os seletores da interface.</summary>
public record CategoryOption(Guid Id, string Name, string? Description);
