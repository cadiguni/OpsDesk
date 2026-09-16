namespace OpsDesk.Domain.Entities;

/// <summary>
/// Feriado excluído da contagem de horas úteis. A data é local ao fuso do expediente
/// (<c>America/Sao_Paulo</c>), por isso é <see cref="DateOnly"/> e não instante.
/// </summary>
public class Holiday
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateOnly Date { get; set; }

    public string Name { get; set; } = null!;
}
