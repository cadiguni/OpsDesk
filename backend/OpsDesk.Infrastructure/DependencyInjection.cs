using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Attachments;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Categories;
using OpsDesk.Application.Dashboard;
using OpsDesk.Application.Notifications;
using OpsDesk.Application.Sla;
using OpsDesk.Application.Tickets;
using OpsDesk.Application.Users;
using OpsDesk.Domain.Entities;
using OpsDesk.Infrastructure.Authentication;
using OpsDesk.Infrastructure.Email;
using OpsDesk.Infrastructure.Persistence;
using OpsDesk.Infrastructure.Persistence.Interceptors;
using OpsDesk.Infrastructure.Persistence.Seed;
using OpsDesk.Infrastructure.Security;
using OpsDesk.Infrastructure.Storage;
using OpsDesk.Infrastructure.Time;

namespace OpsDesk.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "OpsDesk";

    /// <summary>
    /// Registra persistência, tempo e SLA. A API só chama isto e não conhece EF Core nem
    /// Npgsql diretamente.
    /// </summary>
    public static IServiceCollection AddOpsDeskInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // O validador é registrado junto: sem ele o ValidateOnStart abaixo não tem o que
        // validar, e fuso inexistente ou expediente invertido passariam calados.
        services.AddSingleton<IValidateOptions<BusinessHoursOptions>, BusinessHoursOptionsValidator>();

        services
            .AddOptions<BusinessHoursOptions>()
            .Bind(configuration.GetSection(BusinessHoursOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            // Chave de assinatura ausente derruba a subida, não a primeira requisição
            // de login.
            .ValidateOnStart();

        // Credencial do primeiro gestor. Sem ValidateOnStart de propósito: a API sobe
        // normalmente com a seção ausente, que é o caso comum, e a validação acontece
        // quando o comando de bootstrap lê o valor.
        services
            .AddOptions<BootstrapAdminOptions>()
            .Bind(configuration.GetSection(BootstrapAdminOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddMemoryCache();

        services
            .AddOptions<TicketOptions>()
            .Bind(configuration.GetSection(TicketOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<SlaAlertOptions>()
            .Bind(configuration.GetSection(SlaAlertOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<AttachmentStorageOptions>()
            .Bind(configuration.GetSection(AttachmentStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Sem estado por requisição: uma instância serve todas.
        services.AddSingleton<IAttachmentStorage, FileSystemAttachmentStorage>();

        AddCredentialProtection(services);
        AddEmail(services, configuration);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddScoped<IHolidayProvider, CachedHolidayProvider>();

        // A Application enxerga o contexto pela abstração; quem resolve é sempre o
        // OpsDeskDbContext registrado abaixo, com os mesmos interceptors.
        services.AddScoped<IOpsDeskDbContext>(provider =>
            provider.GetRequiredService<OpsDeskDbContext>());

        services.AddScoped<IAccessTokenService, JwtAccessTokenService>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<AuthService>();
        services.AddScoped<TicketService>();
        services.AddSingleton<ReopenPolicy>();
        services.AddScoped<TicketCommentService>();
        services.AddScoped<TicketWorkflowService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<UserDirectoryService>();
        services.AddScoped<UserAdministrationService>();
        services.AddScoped<CategoryAdministrationService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<EmailSettingsService>();
        services.AddScoped<EmailOutboxService>();
        services.AddScoped<IBusinessCalendar, BusinessCalendar>();
        services.AddScoped<SlaClock>();
        services.AddScoped<SlaAlertService>();
        services.AddScoped<DatabaseSeeder>();

        // Interceptors são escopados porque o de histórico depende do usuário da requisição.
        services.AddScoped<TimestampInterceptor>();
        services.AddScoped<TicketHistoryInterceptor>();
        services.AddScoped<TicketNotificationInterceptor>();

        services.AddDbContext<OpsDeskDbContext>((provider, options) => options
            // A connection string é resolvida aqui dentro, quando o contexto é criado, e
            // não durante o registro. Ler configuração no momento do registro congela o
            // valor daquele instante e ignora fontes adicionadas depois — um teste de
            // integração que aponta para outro banco passaria a ser ignorado em silêncio.
            .UseNpgsql(ConnectionString(provider), npgsql => npgsql
                .MigrationsAssembly(typeof(OpsDeskDbContext).Assembly.FullName))
            // Nomes em snake_case para que o banco continue legível em consulta manual,
            // no mesmo espírito de persistir enum como string.
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                provider.GetRequiredService<TimestampInterceptor>(),
                provider.GetRequiredService<TicketHistoryInterceptor>(),
                provider.GetRequiredService<TicketNotificationInterceptor>()));

        return services;
    }

    /// <summary>
    /// Cifra das credenciais guardadas no banco — hoje, o client secret do envio de e-mail.
    ///
    /// O repositório de chaves é configurado pelas options, resolvidas quando o Data
    /// Protection é usado, e não lido do <c>IConfiguration</c> aqui: ler configuração
    /// durante o registro congela o valor daquele instante (ver CLAUDE.md).
    /// </summary>
    private static void AddCredentialProtection(IServiceCollection services)
    {
        services
            .AddOptions<DataProtectionSettings>()
            .BindConfiguration(DataProtectionSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDataProtection().SetApplicationName("OpsDesk");

        services
            .AddOptions<KeyManagementOptions>()
            .Configure<IOptions<DataProtectionSettings>, ILoggerFactory>((keys, settings, loggers) =>
                keys.XmlRepository = new FileSystemXmlRepository(
                    new DirectoryInfo(Path.GetFullPath(settings.Value.KeysPath)), loggers));

        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
    }

    private static void AddEmail(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<EmailDispatchOptions>()
            .Bind(configuration.GetSection(EmailDispatchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Trinta segundos: o despachante envia em lote, e um provedor pendurado não pode
        // prender a rodada inteira.
        services.AddHttpClient<IEmailSender, GraphEmailSender>(client =>
            client.Timeout = TimeSpan.FromSeconds(30));
    }

    private static string ConnectionString(IServiceProvider provider) =>
        provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
            is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' não configurada.");
}
