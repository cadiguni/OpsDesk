using OpsDesk.Application.Abstractions;
using OpsDesk.Infrastructure.Persistence.Seed;

namespace OpsDesk.Api.Maintenance;

/// <summary>
/// Renova os feriados nacionais uma vez por dia: o ano corrente e os dois seguintes.
///
/// Resolve o item do roadmap "feriados não se renovam sozinhos fora de desenvolvimento".
/// Entre as três saídas consideradas — tarefa agendada, seed a cada deploy, tela de
/// feriados —, ficou a tarefa, junto com a tela: deploy não tem data marcada, e depender
/// dele para o calendário andar é depender de alguém lembrar. A tela cobre o que a tabela
/// nacional não tem.
///
/// A primeira rodada é logo na subida, e não depois de um dia: é a subida que costuma
/// acontecer depois de muito tempo parado.
/// </summary>
public class HolidayRenewal(
    IServiceScopeFactory scopes, IConfiguration configuration, ILogger<HolidayRenewal> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lido aqui, na execução, e não na montagem do pipeline: assim a configuração de um
        // teste, registrada depois, vale (ver CLAUDE.md). Zero desliga, como nas outras
        // tarefas em segundo plano.
        var hours = configuration.GetValue("Holidays:RenewalIntervalHours", 24);

        if (hours <= 0)
        {
            logger.LogInformation("Renovação de feriados desligada por configuração.");
            return;
        }

        var interval = TimeSpan.FromHours(hours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();

                var added = await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>()
                    .RenewHolidaysAsync(stoppingToken);

                if (added > 0)
                {
                    scope.ServiceProvider.GetRequiredService<IHolidayCache>().Invalidate();
                    logger.LogInformation("Renovação de feriados: {Count} novo(s).", added);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Banco ainda sem schema, na primeira subida antes do --setup, cai aqui. A
                // próxima rodada tenta de novo.
                logger.LogWarning(ex, "A renovação de feriados falhou; tenta de novo em um dia.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
