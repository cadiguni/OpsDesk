using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpsDesk.Api.Authentication;
using OpsDesk.Api.Authorization;
using OpsDesk.Application.Abstractions;
using OpsDesk.Infrastructure;
using OpsDesk.Infrastructure.Persistence;
using OpsDesk.Infrastructure.Persistence.Seed;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logger mínimo antes do host subir, para que falha de configuração apareça em algum lugar
// em vez de matar o processo em silêncio.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services
        .AddOptions<JwtOptions>()
        .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
        .ValidateDataAnnotations()
        // Chave de assinatura ausente derruba a subida, não a primeira requisição de login.
        .ValidateOnStart();

    builder.Services.AddOpsDeskInfrastructure(builder.Configuration);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
        ?? throw new InvalidOperationException("Seção 'Jwt' não configurada.");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                // Token de acesso é curto; tolerar cinco minutos de desvio anularia isso.
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    builder.Services.AddAuthorizationBuilder().AddOpsDeskPolicies();

    // Rate limiting nos endpoints anônimos, em especial no login: é onde a força bruta bate.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddFixedWindowLimiter(RateLimitPolicies.Anonymous, limiter =>
        {
            limiter.PermitLimit = 20;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
        });
    });

    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // O refresh token viaja em cookie httpOnly, e cookie entre origens exige isto.
        .AllowCredentials()));

    builder.Services.AddOpenApi();

    builder.Services
        .AddHealthChecks()
        .AddDbContextCheck<OpsDeskDbContext>("postgres");

    builder.Services.AddProblemDetails();

    var app = builder.Build();

    await app.ApplyDatabaseStartupTasksAsync();

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    // TLS termina na borda (Container Apps / Front Door), por isso não há
    // UseHttpsRedirection: dentro do container a API fala HTTP, e redirecionar aqui
    // quebraria o health check do orquestrador.

    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health").AllowAnonymous();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "OpsDesk API"));
    }

    Log.Information("OpsDesk API iniciada no ambiente {Environment}.", app.Environment.EnvironmentName);

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "A API encerrou durante a inicialização.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Nomes das políticas de rate limiting.</summary>
internal static class RateLimitPolicies
{
    public const string Anonymous = "anonymous";
}

internal static class StartupTasks
{
    /// <summary>
    /// Aplica migrations e o seed de dados de referência.
    ///
    /// Migration automática só em desenvolvimento: em produção o schema sobe como etapa
    /// explícita do deploy, para que ninguém descubra uma alteração de banco pelo log de
    /// inicialização. Invariante 7 do CLAUDE.md — nunca EnsureCreated, sempre migration.
    /// </summary>
    public static async Task ApplyDatabaseStartupTasksAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<OpsDeskDbContext>();
        await db.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(includeDevelopmentUsers: true);
    }
}
