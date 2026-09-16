using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace OpsDesk.Api.RateLimiting;

/// <summary>
/// Rate limiting dos endpoints anônimos, em especial o login — é onde a força bruta bate.
/// </summary>
public class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Tentativas por minuto e por IP nos endpoints anônimos.
    ///
    /// Configurável porque o número certo depende do ambiente: atrás de NAT corporativo
    /// muitas pessoas compartilham o mesmo endereço, e um limite apertado deixaria de ser
    /// proteção para virar incidente. Vinte é folgado para uso humano e apertado para
    /// script.
    /// </summary>
    public int AnonymousPermitsPerMinute { get; set; } = 20;
}

public static class RateLimitPolicies
{
    public const string Anonymous = "anonymous";

    public static RateLimiterOptions AddOpsDeskPolicies(this RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(Anonymous, context => RateLimitPartition.GetFixedWindowLimiter(
            // Particionado por IP de origem, não global. Um limitador único para toda a
            // aplicação seria pior do que não ter: quem estivesse testando senhas
            // consumiria a cota de todo mundo e trancaria os usuários legítimos para fora,
            // transformando a proteção contra força bruta em negação de serviço de graça.
            partitionKey: PartitionKey(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                // Lido por DI, e não capturado na configuração do middleware. Ler
                // IConfiguration direto durante a montagem do pipeline pega o valor antes
                // de fontes adicionadas depois — foi assim que a suíte de testes viu o
                // limite de produção mesmo tendo configurado outro.
                PermitLimit = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitOptions>>()
                    .Value.AnonymousPermitsPerMinute,

                Window = TimeSpan.FromMinutes(1),

                // Sem fila: quem passou do limite recebe 429 na hora. Enfileirar tentativa
                // de login só atrasaria a resposta de erro e prenderia thread do servidor.
                QueueLimit = 0
            }));

        return options;
    }

    private static string PartitionKey(HttpContext context)
    {
        // X-Forwarded-For seria necessário atrás de proxy, mas confiar nesse cabeçalho sem
        // o middleware de forwarded headers configurado é pior: o cliente escolheria a
        // própria partição e o limite deixaria de existir. Quando a API for para trás do
        // Front Door, configure UseForwardedHeaders e este endereço passa a ser o real.
        return context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
    }
}
