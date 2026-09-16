using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Sla;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Entities;
using OpsDesk.Infrastructure.Authentication;
using OpsDesk.Infrastructure.Persistence;
using OpsDesk.Infrastructure.Persistence.Interceptors;
using OpsDesk.Infrastructure.Persistence.Seed;
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

        services.AddMemoryCache();

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
        services.AddScoped<IBusinessCalendar, BusinessCalendar>();
        services.AddScoped<SlaClock>();
        services.AddScoped<DatabaseSeeder>();

        // Interceptors são escopados porque o de histórico depende do usuário da requisição.
        services.AddScoped<TimestampInterceptor>();
        services.AddScoped<TicketHistoryInterceptor>();

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
                provider.GetRequiredService<TicketHistoryInterceptor>()));

        return services;
    }

    private static string ConnectionString(IServiceProvider provider) =>
        provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
            is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' não configurada.");
}
