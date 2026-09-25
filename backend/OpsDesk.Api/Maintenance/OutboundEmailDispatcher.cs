using Microsoft.Extensions.Options;
using OpsDesk.Application.Notifications;

namespace OpsDesk.Api.Maintenance;

/// <summary>
/// Envia a fila de saída de e-mail em intervalos curtos.
///
/// Roda no processo da API, como a varredura de anexos. Com várias réplicas, duas podem
/// pegar o mesmo e-mail na mesma rodada e enviá-lo duas vezes: aceitável enquanto a API
/// roda numa réplica só, que é o caso hoje. Com mais de uma, o caminho é
/// <c>DispatchIntervalSeconds = 0</c> em todas menos uma, ou trava por linha
/// (<c>FOR UPDATE SKIP LOCKED</c>) na seleção.
/// </summary>
public class OutboundEmailDispatcher(
    IServiceScopeFactory scopes,
    IOptions<EmailDispatchOptions> options,
    ILogger<OutboundEmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (settings.DispatchIntervalSeconds <= 0)
        {
            logger.LogInformation("Envio da fila de e-mail desligado por configuração.");
            return;
        }

        var interval = TimeSpan.FromSeconds(settings.DispatchIntervalSeconds);

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

                var outbox = scope.ServiceProvider.GetRequiredService<EmailOutboxService>();
                var sent = await outbox.DispatchAsync(settings.BatchSize, stoppingToken);

                if (sent > 0)
                {
                    logger.LogInformation("Fila de e-mail: {Count} enviado(s).", sent);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Falha de rodada não derruba a API. Falha de entrega de um e-mail já é
                // tratada dentro do DispatchAsync; chegar aqui é erro de outra natureza —
                // banco fora, por exemplo — e a próxima rodada tenta de novo.
                logger.LogError(ex, "A rodada de envio da fila de e-mail falhou.");
            }
        }
    }
}
