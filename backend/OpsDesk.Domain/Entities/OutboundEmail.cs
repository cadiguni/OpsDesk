using OpsDesk.Domain.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// E-mail na fila de saída.
///
/// Nasce na mesma transação da mudança que o motivou, e um serviço em segundo plano o
/// envia. Assim nenhuma notificação se perde entre gravar a mudança e enviar, e nenhum
/// comentário falha porque o provedor de e-mail está fora do ar.
///
/// O corpo é gravado já pronto. Montá-lo na hora do envio leria o chamado como ele estiver
/// então, e não como estava quando o evento aconteceu.
/// </summary>
public class OutboundEmail : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? TicketId { get; set; }

    public string ToAddress { get; set; } = null!;

    public string ToName { get; set; } = null!;

    public string Subject { get; set; } = null!;

    public string HtmlBody { get; set; } = null!;

    public OutboundEmailStatus Status { get; set; } = OutboundEmailStatus.Pending;

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
