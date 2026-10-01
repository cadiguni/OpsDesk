using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Regra de classificação da caixa de entrada, mantida pela equipe. Nasce sempre de um
/// gesto explícito na caixa de SPAM — nada é aprendido em silêncio a partir de um clique.
/// </summary>
public class SpamRule : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public SpamRuleType Type { get; set; }

    /// <summary>Em minúsculas. Endereço, domínio ou trecho de texto, conforme o tipo.</summary>
    public string Pattern { get; set; } = null!;

    public SpamRuleAction Action { get; set; }

    public Guid? CreatedById { get; set; }

    public User? CreatedBy { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
