using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpsDesk.Api.Authentication;
using OpsDesk.Api.Authorization;
using OpsDesk.Api.Endpoints;
using OpsDesk.Api.RateLimiting;
using OpsDesk.Api.Validation;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Auth;
using OpsDesk.Infrastructure;
using OpsDesk.Infrastructure.Authentication;
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

    builder.Services.AddOpsDeskInfrastructure(builder.Configuration);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

    // Os validators vivem na Application; o registro varre aquele assembly.
    builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

    // O JwtOptions vem por DI, e não de uma leitura direta do IConfiguration aqui.
    // Ler configuração durante a montagem do pipeline captura o valor daquele instante e
    // ignora fontes registradas depois — é o que faz a configuração de um teste de
    // integração não surtir efeito, sem nenhum erro para indicar o problema.
    builder.Services
        .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
        {
            var jwt = jwtOptions.Value;

            // Sem remapeamento de nomes de claim. Com ele ligado, "sub" viraria a URI
            // longa do ClaimTypes na entrada e a leitura por nome curto falharia em
            // silêncio — é a causa clássica de "a claim está no token mas não chega".
            bearer.MapInboundClaims = false;

            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

                // Os nomes que o RequireRole e o User.Identity.Name vão procurar.
                NameClaimType = OpsDeskClaims.Name,
                RoleClaimType = OpsDeskClaims.Role,

                // Token de acesso é curto; tolerar cinco minutos de desvio anularia isso.
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    builder.Services.AddAuthorizationBuilder().AddOpsDeskPolicies();

    builder.Services
        .AddOptions<RateLimitOptions>()
        .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName));

    builder.Services.AddRateLimiter(options => options.AddOpsDeskPolicies());

    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        // O refresh token viaja em cookie httpOnly, e cookie entre origens exige isto.
        .AllowCredentials()));

    // Enum como texto no JSON, e não como inteiro.
    //
    // Mesma razão de persistir enum como string no banco: "Technician" se lê, 1 não. E
    // tem consequência prática — os tipos do frontend são gerados do schema OpenAPI, e com
    // inteiro o TypeScript receberia `role: number`, perdendo a união fechada de valores
    // que hoje garante em tempo de compilação que todo perfil tem rótulo na interface.
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    // Teto do multipart acima do limite por arquivo do AttachmentService, de propósito:
    // arquivo entre um e outro é recusado pelo serviço, com 413 e mensagem dizendo qual
    // é o limite. Fosse o teto igual, o framework cortaria antes e a pessoa receberia um
    // erro genérico de pedido malformado.
    builder.Services.Configure<FormOptions>(options =>
        options.MultipartBodyLengthLimit = 32 * 1024 * 1024);

    builder.Services.AddOpenApi();

    builder.Services
        .AddHealthChecks()
        .AddDbContextCheck<OpsDeskDbContext>("postgres");

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<MalformedRequestHandler>();

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
    app.MapAuthEndpoints();
    app.MapTicketEndpoints();
    app.MapAttachmentEndpoints();
    app.MapDashboardEndpoints();

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
