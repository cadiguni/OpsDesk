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
///
/// Seguro com várias réplicas da API: cada e-mail é reservado antes do envio (ver
/// <see cref="ClaimLease"/>), e só quem reservou envia.
/// </summary>
public class EmailOutboxService(IOpsDeskDbContext db, EmailSettingsService settingsService, IClock clock)
{
    /// <summary>Espera antes de cada nova tentativa, em minutos. Depois da última, desiste.</summary>
    public static readonly int[] RetryDelaysInMinutes = [1, 5, 15, 60];

    public static int MaxAttempts => RetryDelaysInMinutes.Length + 1;

    public const string RedactedBody = "(conteúdo descartado depois do envio)";

    /// <summary>
    /// Por quanto tempo um e-mail fica reservado para quem o pegou. Se essa réplica cair no
    /// meio do envio, o e-mail volta à fila sozinho depois disso.
    /// </summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

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
            // Reserva antes de enviar. Com duas réplicas, as duas leem o mesmo e-mail na
            // mesma rodada; só a que conseguir mover o NextAttemptAt — condicionado ao valor
            // que ela leu — envia. A outra encontra zero linhas e segue.
            var claimed = await db.OutboundEmails
                .Where(e => e.Id == email.Id
                            && e.Status == OutboundEmailStatus.Pending
                            && e.NextAttemptAt == email.NextAttemptAt)
                .ExecuteUpdateAsync(set => set.SetProperty(e => e.NextAttemptAt, now + ClaimLease), cancellationToken);

            if (claimed == 0)
            {
                continue;
            }

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

            // E-mail com link de definir senha não fica guardado depois de resolvido: quem
            // lesse a tabela poderia usar o link ainda dentro da validade.
            if (email.IsSensitive && email.Status != OutboundEmailStatus.Pending)
            {
                email.HtmlBody = RedactedBody;
            }

            // Grava a cada e-mail: se o processo cair no meio da rodada, o que já saiu
            // não sai de novo.
            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }
}
