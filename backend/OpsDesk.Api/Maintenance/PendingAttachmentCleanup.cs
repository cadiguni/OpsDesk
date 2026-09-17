using Microsoft.Extensions.Options;
using OpsDesk.Application.Attachments;
using OpsDesk.Infrastructure.Storage;

namespace OpsDesk.Api.Maintenance;

/// <summary>
/// Varredura periódica dos anexos pendentes abandonados.
///
/// Anexo pendente é invisível para todos menos para quem o enviou, então arquivo
/// abandonado não aparece em tela nenhuma: sem esta varredura, o espaço vaza em silêncio
/// e a primeira notícia vem na conta do armazenamento.
///
/// Roda no processo da API, e não como job separado, porque é uma consulta por índice a
/// cada seis horas — infraestrutura própria para isso custaria mais que o problema. Com
/// várias réplicas, todas varrem: o lote é pequeno, a operação é idempotente e a corrida
/// entre duas réplicas termina com uma delas não achando nada. Se isso passar a
/// incomodar, `CleanUpIntervalHours = 0` desliga a varredura e a limpeza migra para um
/// job com dono único.
/// </summary>
public class PendingAttachmentCleanup(
    IServiceScopeFactory scopes,
    IOptions<AttachmentStorageOptions> options,
    ILogger<PendingAttachmentCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (settings.CleanUpIntervalHours <= 0)
        {
            logger.LogInformation("Varredura de anexos pendentes desligada por configuração.");
            return;
        }

        var interval = TimeSpan.FromHours(settings.CleanUpIntervalHours);
        var retention = TimeSpan.FromHours(settings.PendingRetentionHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            // A espera vem antes da primeira varredura de propósito: a subida da aplicação
            // já tem migration e seed para fazer, e um anexo de poucos segundos de vida
            // nunca é candidato a limpeza.
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

                var attachments = scope.ServiceProvider.GetRequiredService<AttachmentService>();
                var removed = await attachments.CleanUpPendingAsync(retention, stoppingToken);

                if (removed > 0)
                {
                    logger.LogInformation(
                        "Varredura removeu {Count} anexo(s) pendente(s) com mais de {Hours}h.",
                        removed, settings.PendingRetentionHours);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Falha de varredura não derruba a API: o próximo ciclo tenta de novo, e o
                // pior caso é espaço em disco sobrando ocupado por mais seis horas.
                logger.LogError(ex, "A varredura de anexos pendentes falhou.");
            }
        }
    }
}
