using OpsDesk.Domain.Common;

namespace OpsDesk.Domain.Entities;

/// <summary>Categoria do chamado. Escolhida pelo solicitante na abertura.</summary>
public class Category : IHasUpdatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Ticket> Tickets { get; set; } = [];
}
