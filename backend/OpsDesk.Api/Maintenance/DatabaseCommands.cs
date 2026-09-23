using Microsoft.EntityFrameworkCore;
using OpsDesk.Infrastructure.Persistence;
using OpsDesk.Infrastructure.Persistence.Seed;

namespace OpsDesk.Api.Maintenance;

/// <summary>
/// Instalação do banco como etapa explícita de deploy.
///
/// Em Development a API aplica migration e seed na subida, e isso esconde um problema:
/// fora de Development nada disso acontecia e não havia comando para fazer acontecer.
/// Uma instalação limpa subia saudável — o <c>/health</c> respondia, porque o banco
/// respondia — e recusava o primeiro chamado com 500, porque não havia política de SLA.
///
/// Os comandos ficam no mesmo executável, e não num utilitário separado, por um motivo
/// prático: a imagem do contêiner já é essa. Instalar é
/// <c>docker compose run --rm api --setup</c>, sem publicar uma segunda imagem só para
/// carregar as mesmas migrations.
///
/// Migration continua fora da subida da aplicação, pela razão de sempre: ninguém deve
/// descobrir uma alteração de schema lendo log de inicialização.
/// </summary>
internal static class DatabaseCommands
{
    /// <summary>Aplica as migrations pendentes.</summary>
    private const string Migrate = "--migrate";

    /// <summary>Insere categorias, políticas de SLA e feriados.</summary>
    private const string Seed = "--seed";

    /// <summary>Cria o primeiro gestor a partir de OpsDesk:Bootstrap.</summary>
    private const string BootstrapAdmin = "--bootstrap-admin";

    /// <summary>Os três acima, na ordem em que dependem um do outro.</summary>
    private const string Setup = "--setup";

    private static readonly string[] Known = [Migrate, Seed, BootstrapAdmin, Setup];

    /// <summary>Há comando de manutenção na linha de comando?</summary>
    public static bool Requested(string[] args) =>
        args.Any(arg => Known.Contains(arg, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Executa os comandos pedidos e devolve o código de saída do processo.
    ///
    /// Roda sem subir o servidor HTTP: o processo faz a tarefa e termina, que é o que um
    /// job de deploy espera. Os serviços em segundo plano da aplicação — a varredura de
    /// anexos pendentes, por exemplo — não chegam a iniciar, porque nunca chamamos
    /// <c>RunAsync</c>.
    /// </summary>
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var requested = new HashSet<string>(
            args.Where(arg => Known.Contains(arg, StringComparer.OrdinalIgnoreCase))
                .Select(arg => arg.ToLowerInvariant()));

        var all = requested.Contains(Setup);

        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DatabaseCommands));

        await using var scope = app.Services.CreateAsyncScope();

        try
        {
            if (all || requested.Contains(Migrate))
            {
                var db = scope.ServiceProvider.GetRequiredService<OpsDeskDbContext>();

                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

                if (pending.Count == 0)
                {
                    logger.LogInformation("Schema já está na versão da aplicação.");
                }
                else
                {
                    logger.LogInformation(
                        "Aplicando {Count} migration(s): {Migrations}.",
                        pending.Count, string.Join(", ", pending));

                    await db.Database.MigrateAsync();
                }
            }

            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

            if (all || requested.Contains(Seed))
            {
                // Sem usuários de exemplo: são credenciais conhecidas, e este comando
                // existe justamente para rodar fora de desenvolvimento.
                await seeder.SeedAsync(includeDevelopmentUsers: false);

                logger.LogInformation("Dados de referência conferidos.");
            }

            if (all || requested.Contains(BootstrapAdmin))
            {
                var result = await seeder.EnsureBootstrapAdminAsync();

                // Pedir o bootstrap explicitamente e não ter configuração é engano de
                // quem chamou, e engano silencioso aqui custa caro: o deploy segue, e a
                // falta do gestor só aparece quando alguém tenta usar o sistema.
                if (result == BootstrapAdminResult.NotConfigured && !all)
                {
                    logger.LogError(
                        "{Command} pedido sem {Section}:Email e {Section}:Password. " +
                        "Defina OpsDesk__Bootstrap__Email e OpsDesk__Bootstrap__Password.",
                        BootstrapAdmin, BootstrapAdminOptions.SectionName,
                        BootstrapAdminOptions.SectionName);

                    return 1;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A instalação do banco falhou.");

            return 1;
        }
    }
}
