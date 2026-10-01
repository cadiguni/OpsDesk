using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Inbound;

/// <summary>
/// Retenção dos e-mails recebidos (docs/integracao-email.md, seção 5.4): spam some depois
/// de noventa dias, duplicata depois de trinta, e o que virou chamado perde o corpo depois
/// de um ano — o conteúdo relevante já está no chamado, e guardar duas vezes é guardar o
/// dobro de dado pessoal.
/// </summary>
public class InboundRetentionService(IOpsDeskDbContext db, IClock clock)
{
    public static readonly TimeSpan SpamRetention = TimeSpan.FromDays(90);
    public static readonly TimeSpan DuplicateRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan ProcessedBodyRetention = TimeSpan.FromDays(365);

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var spamCutoff = now - SpamRetention;
        var duplicateCutoff = now - DuplicateRetention;
        var processedCutoff = now - ProcessedBodyRetention;

        var removed = await db.InboundEmails
            .Where(e => (e.Status == InboundEmailStatus.Spam && e.ReceivedAt < spamCutoff)
                        || (e.Status == InboundEmailStatus.Duplicate && e.ReceivedAt < duplicateCutoff))
            .ExecuteDeleteAsync(cancellationToken);

        var trimmed = await db.InboundEmails
            .Where(e => e.Status == InboundEmailStatus.Processed && e.ReceivedAt < processedCutoff && e.BodyText != "")
            .ExecuteUpdateAsync(set => set
                .SetProperty(e => e.BodyText, "")
                .SetProperty(e => e.HeadersJson, (string?)null), cancellationToken);

        return removed + trimmed;
    }
}
