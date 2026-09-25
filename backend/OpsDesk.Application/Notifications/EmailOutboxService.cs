using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Notifications;

/// <summary>
/// Envio da fila de saída. Chamado periodicamente pelo despachante em segundo plano, e
/// diretamente pelos testes.
///
/// Com o envio desligado, ou sem credencial legível, nada sai e nada é descartado: os
/// e-mails já na fila esperam. Mas nenhum e-mail novo entra na fila enquanto o envio
/// estiver desligado — ligar o envio não dispara uma avalanche de avisos antigos.
/// </summary>
public class EmailOutboxService(IOpsDeskDbContext db, EmailSettingsService settingsService, IClock clock)
{
    /// <summary>Espera antes de cada nova tentativa, em minutos. Depois da última, desiste.</summary>
    public static readonly int[] RetryDelaysInMinutes = [1, 5, 15, 60];

    public static int MaxAttempts => RetryDelaysInMinutes.Length + 1;

    /// <returns>Quantos e-mails foram enviados nesta rodada.</returns>
    public async Task<int> DispatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var settings = await db.EmailSettings.SingleOrDefaultAsync(cancellationToken);

        if (settingsService.LoadCredentials(settings) is not { } credentials)
        {
            return 0;
        }

        var now = clock.UtcNow;

        var due = await db.OutboundEmails
            .Where(e => e.Status == OutboundEmailStatus.Pending && e.NextAttemptAt <= now)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var sent = 0;

        foreach (var email in due)
        {
            var error = await settingsService.TrySendAsync(
                settings!,
                credentials,
                new EmailMessage(email.ToAddress, email.ToName, email.Subject, email.HtmlBody),
                cancellationToken);

            email.Attempts++;

            if (error is null)
            {
                email.Status = OutboundEmailStatus.Sent;
                email.SentAt = clock.UtcNow;
                email.LastError = null;
                sent++;
            }
            else if (email.Attempts >= MaxAttempts)
            {
                email.Status = OutboundEmailStatus.Failed;
                email.LastError = error;
            }
            else
            {
                email.NextAttemptAt = clock.UtcNow.AddMinutes(RetryDelaysInMinutes[email.Attempts - 1]);
                email.LastError = error;
            }

            // Grava a cada e-mail: se o processo cair no meio da rodada, o que já saiu
            // não sai de novo.
            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }
}
