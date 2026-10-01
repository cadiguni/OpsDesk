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

    /// <summary>
    /// Desligado, o dia volta a ser útil. Feriado nacional não é apagado, é desligado: a
    /// renovação anual recria toda data nacional que não existir, e apagar faria o feriado
    /// voltar no dia seguinte, contra a decisão de quem o tirou.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Cadastrado pelo gestor — feriado municipal, ponte, recesso. Só esses podem ser
    /// apagados; os nacionais vêm da tabela do sistema.
    /// </summary>
    public bool IsCustom { get; set; }
}
