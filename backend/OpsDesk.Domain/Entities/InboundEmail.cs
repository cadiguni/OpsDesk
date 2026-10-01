using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Mensagem recebida, guardada como veio (docs/integracao-email.md, seção 3).
///
/// E-mail recebido e chamado são coisas separadas: toda mensagem vira um registro aqui,
/// seja spam, duplicata ou chamado novo. O chamado é uma consequência possível da
/// mensagem, não a mensagem — e por isso spam nunca chega perto da base de chamados.
/// </summary>
public class InboundEmail : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid EmailAccountId { get; set; }

    public EmailAccount EmailAccount { get; set; } = null!;

    /// <summary>Cabeçalho Message-ID. É o que identifica reentrega do mesmo e-mail.</summary>
    public string? MessageId { get; set; }

    public string? InReplyTo { get; set; }

    public string? References { get; set; }

    public string FromName { get; set; } = null!;

    public string FromAddress { get; set; } = null!;

    public string? ToAddress { get; set; }

    public string Subject { get; set; } = null!;

    /// <summary>
    /// Corpo em texto puro, pedido assim ao provedor. HTML de e-mail não é guardado nem
    /// mostrado: na caixa de SPAM ele seria o vetor mais óbvio de conteúdo malicioso.
    /// </summary>
    public string BodyText { get; set; } = null!;

    /// <summary>
    /// Cabeçalhos que o processamento consulta — resposta automática, autenticação do
    /// remetente —, em JSON. Guardados para que o reprocessamento decida igual à primeira vez.
    /// </summary>
    public string? HeadersJson { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public InboundEmailStatus Status { get; set; } = InboundEmailStatus.Pending;

    public int SpamScore { get; set; }

    /// <summary>Qual regra decidiu. Sempre preenchido quando a mensagem vai para o SPAM.</summary>
    public string? SpamReason { get; set; }

    public Guid? TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public string? ProcessingError { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>Quem revisou na caixa de SPAM, e quando.</summary>
    public Guid? ReviewedById { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
