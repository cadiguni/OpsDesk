using Microsoft.Extensions.Options;
using OpsDesk.Application.Sla;

namespace OpsDesk.Api.Maintenance;

/// <summary>
/// Verifica os prazos de SLA em intervalos curtos e gera os alertas.
///
/// Roda no processo da API, como a varredura de anexos e o envio de e-mail. Com várias
/// réplicas, todas verificam, e o índice único de <c>SlaAlert</c> é o que garante um aviso
/// só — a segunda réplica a gravar recebe a recusa e segue.
/// </summary>
public class SlaAlertScheduler(
    IServiceScopeFactory scopes,
    IOptions<SlaAlertOptions> options,
    ILogger<SlaAlertScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.IntervalMinutes <= 0)
        {
            logger.LogInformation("Alertas de SLA desligados por configuração.");
            return;
        }

        var interval = TimeSpan.FromMinutes(options.Value.IntervalMinutes);

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

                var alerts = scope.ServiceProvider.GetRequiredService<SlaAlertService>();
                var sent = await alerts.RunAsync(stoppingToken);

                if (sent > 0)
                {
                    logger.LogInformation("Alertas de SLA: {Count} novo(s).", sent);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A verificação de alertas de SLA falhou.");
            }
        }
    }
}
