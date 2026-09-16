using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Sla;
using OpsDesk.Domain.Entities;
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
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' não configurada.");

        services
            .AddOptions<BusinessHoursOptions>()
            .Bind(configuration.GetSection(BusinessHoursOptions.SectionName))
            .ValidateOnStart();

        services.AddMemoryCache();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddScoped<IHolidayProvider, CachedHolidayProvider>();
        services.AddScoped<IBusinessCalendar, BusinessCalendar>();
        services.AddScoped<SlaClock>();
        services.AddScoped<DatabaseSeeder>();

        // Interceptors são escopados porque o de histórico depende do usuário da requisição.
        services.AddScoped<TimestampInterceptor>();
        services.AddScoped<TicketHistoryInterceptor>();

        services.AddDbContext<OpsDeskDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(OpsDeskDbContext).Assembly.FullName))
            // Nomes em snake_case para que o banco continue legível em consulta manual,
            // no mesmo espírito de persistir enum como string.
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                provider.GetRequiredService<TimestampInterceptor>(),
                provider.GetRequiredService<TicketHistoryInterceptor>()));

        return services;
    }
}
