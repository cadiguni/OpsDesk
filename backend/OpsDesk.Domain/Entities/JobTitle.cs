using OpsDesk.Domain.Common;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Cargo da pessoa na empresa: Tech Lead, Analista, Estagiário.
///
/// Só informativo. O que a pessoa pode fazer no portal é o <see cref="User.Role"/>, e os
/// dois são independentes de propósito: um analista pode administrar o portal e outro não,
/// sem que o catálogo de cargos precise de "Analista administrador".
/// </summary>
public class JobTitle : IHasUpdatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<User> Users { get; set; } = [];
}
