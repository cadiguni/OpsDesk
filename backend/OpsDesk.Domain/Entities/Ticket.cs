using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>Chamado. Entidade central do sistema.</summary>
public class Ticket : IHasUpdatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Código legível no formato <c>OPS-000123</c>. Vem da sequence <c>ticket_code_seq</c>
    /// do PostgreSQL, lida na mesma transação da inserção. Nunca de <c>COUNT</c> ou <c>MAX</c>.
    /// </summary>
    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Description { get; set; } = null!;

    public TicketStatus Status { get; set; } = TicketStatus.Open;

    public TicketPriority Priority { get; set; }

    public Guid RequesterId { get; set; }

    public User Requester { get; set; } = null!;

    public Guid? AssignedTechnicianId { get; set; }

    public User? AssignedTechnician { get; set; }

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public TicketSource Source { get; set; } = TicketSource.Portal;

    /// <summary>
    /// Identificador na origem externa, como o <c>Message-ID</c> do e-mail que gerou o chamado.
    /// Nulo quando a origem é o portal.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>Prazo de resposta, calculado uma única vez na criação.</summary>
    public DateTimeOffset SlaResponseDueAt { get; set; }

    /// <summary>
    /// Prazo de resolução. Calculado na criação e recalculado ao sair de
    /// "Aguardando usuário", somando o período pausado em horas úteis.
    /// </summary>
    public DateTimeOffset SlaResolutionDueAt { get; set; }

    /// <summary>Primeiro comentário público de técnico ou gestor. Encerra o SLA de resposta.</summary>
    public DateTimeOffset? FirstRespondedAt { get; set; }

    /// <summary>Instante em que o chamado entrou em "Aguardando usuário". Nulo quando não está pausado.</summary>
    public DateTimeOffset? SlaPausedAt { get; set; }

    /// <summary>Total de minutos úteis já acumulados em pausa.</summary>
    public int SlaPausedBusinessMinutes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public ICollection<TicketComment> Comments { get; set; } = [];

    public ICollection<TicketHistory> History { get; set; } = [];

    /// <summary>Status finais não contam mais SLA.</summary>
    public bool IsClosedOut => Status is TicketStatus.Resolved or TicketStatus.Closed or TicketStatus.Cancelled;

    /// <summary>Chamado cancelado fica fora dos indicadores de SLA.</summary>
    public bool CountsForSla => Status != TicketStatus.Cancelled;

    public bool IsResponseOverdue(DateTimeOffset now) =>
        CountsForSla && FirstRespondedAt is null && now > SlaResponseDueAt;

    public bool IsResolutionOverdue(DateTimeOffset now) =>
        CountsForSla && ResolvedAt is null && now > SlaResolutionDueAt;
}
