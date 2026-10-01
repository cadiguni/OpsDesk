using OpsDesk.Application.Inbound;

namespace OpsDesk.Api.Maintenance;

/// <summary>
/// Lê as caixas monitoradas a cada minuto (<c>Mailbox:IntervalSeconds</c>) e aplica a
/// retenção uma vez por dia (<c>Mailbox:RetentionIntervalHours</c>). Zero desliga cada um.
///
/// Com várias réplicas, todas leem: a mesma mensagem lida por duas vira duplicata na
/// segunda, pela verificação de Message-ID, e não chamado em dobro.
/// </summary>
public class MailboxPoller(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<MailboxPoller> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lido aqui, na execução: a configuração de teste, registrada depois da montagem,
        // precisa valer (ver CLAUDE.md).
        var seconds = configuration.GetValue("Mailbox:IntervalSeconds", 60);
        var retentionHours = configuration.GetValue("Mailbox:RetentionIntervalHours", 24);

        if (seconds <= 0)
        {
            logger.LogInformation("Leitura das caixas monitoradas desligada por configuração.");
            return;
        }

        var interval = TimeSpan.FromSeconds(seconds);
        var lastRetention = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();

                var read = await scope.ServiceProvider.GetRequiredService<MailboxSyncService>().SyncAsync(cancellationToken: stoppingToken);

                if (read > 0)
                {
                    logger.LogInformation("Caixas monitoradas: {Count} mensagem(ns) nova(s).", read);
                }

                if (retentionHours > 0 && DateTimeOffset.UtcNow - lastRetention > TimeSpan.FromHours(retentionHours))
                {
                    await scope.ServiceProvider.GetRequiredService<InboundRetentionService>().RunAsync(stoppingToken);
                    lastRetention = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A leitura das caixas monitoradas falhou.");
            }
        }
    }
}
