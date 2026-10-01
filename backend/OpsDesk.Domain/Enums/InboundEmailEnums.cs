namespace OpsDesk.Domain.Enums;

/// <summary>Destino de uma mensagem recebida. Persistido como string.</summary>
public enum InboundEmailStatus
{
    Pending,

    /// <summary>Virou chamado novo ou comentário num chamado existente.</summary>
    Processed,

    Spam,

    /// <summary>Reentrega de mensagem já recebida (mesmo Message-ID na mesma caixa).</summary>
    Duplicate,

    /// <summary>O processamento falhou; há nova tentativa automática e reprocessamento manual.</summary>
    Failed
}

public enum SpamRuleType
{
    SenderAddress,
    SenderDomain,
    SubjectContains,
    BodyContains
}

public enum SpamRuleAction
{
    Allow,
    Block
}
