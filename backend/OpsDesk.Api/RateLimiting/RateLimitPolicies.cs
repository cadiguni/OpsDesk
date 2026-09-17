using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OpsDesk.Application.Auth;

namespace OpsDesk.Api.RateLimiting;

public class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Tentativas de login e de cadastro por minuto, por IP.
    ///
    /// Configurável porque o número certo depende do ambiente: atrás de NAT corporativo
    /// muitas pessoas compartilham o mesmo endereço, e um limite apertado deixaria de ser
    /// proteção para virar incidente. Vinte é folgado para uso humano e apertado para
    /// script.
    /// </summary>
    public int CredentialAttemptsPerMinute { get; set; } = 20;

    /// <summary>
    /// Renovações de sessão por minuto, por IP.
    ///
    /// Cota separada e bem mais larga que a de credencial, por dois motivos. Primeiro, o
    /// perfil de uso é outro: a interface renova a sessão a cada carregamento de página, e
    /// uma pessoa recarregando algumas vezes seguidas é comportamento normal, não ataque.
    /// Segundo, e mais importante, cota compartilhada com o login criava uma falha real —
    /// as renovações esgotavam a cota e o usuário legítimo recebia "muitas tentativas" na
    /// primeira vez que digitava a senha, sem nunca ter errado nada.
    ///
    /// Aqui não há o que adivinhar por força bruta: quem chama apresenta um token de 256
    /// bits, e o limite existe só para conter repetição abusiva.
    /// </summary>
    public int RefreshAttemptsPerMinute { get; set; } = 120;

    /// <summary>
    /// Envios de anexo por minuto, por usuário autenticado.
    ///
    /// Aqui a cota não é contra força bruta: é contra consumo. Cada envio aceito grava até
    /// dez megabytes no armazenamento, e sem limite um único usuário autenticado enche o
    /// volume — ou a fatura, quando isso for para a nuvem. Trinta por minuto cobre com
    /// folga anexar uma dezena de prints de uma vez e corta o laço que sobe arquivo sem
    /// parar.
    /// </summary>
    public int UploadsPerMinute { get; set; } = 30;
}

public static class RateLimitPolicies
{
    /// <summary>Login e cadastro. É onde a força bruta bate.</summary>
    public const string Credentials = "credentials";

    /// <summary>Renovação de sessão.</summary>
    public const string Refresh = "refresh";

    /// <summary>Envio de anexo. Cota de consumo, e não de tentativa.</summary>
    public const string Uploads = "uploads";

    public static RateLimiterOptions AddOpsDeskPolicies(this RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(Credentials, context => Partition(
            context, Credentials, o => o.CredentialAttemptsPerMinute));

        options.AddPolicy(Refresh, context => Partition(
            context, Refresh, o => o.RefreshAttemptsPerMinute));

        options.AddPolicy(Uploads, context => Partition(
            context, Uploads, o => o.UploadsPerMinute));

        return options;
    }

    private static RateLimitPartition<string> Partition(
        HttpContext context, string policy, Func<RateLimitOptions, int> permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Particionado por IP de origem, não global. Um limitador único para toda a
            // aplicação seria pior do que não ter: quem estivesse testando senhas
            // consumiria a cota de todo mundo e trancaria os usuários legítimos para fora,
            // transformando a proteção contra força bruta em negação de serviço de graça.
            partitionKey: PartitionKey(context, policy),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                // Lido por DI, e não capturado na configuração do middleware. Ler
                // IConfiguration direto durante a montagem do pipeline pega o valor antes
                // de fontes adicionadas depois — foi assim que a suíte de testes viu o
                // limite de produção mesmo tendo configurado outro.
                PermitLimit = permitLimit(
                    context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value),

                Window = TimeSpan.FromMinutes(1),

                // Sem fila: quem passou do limite recebe 429 na hora. Enfileirar tentativa
                // de login só atrasaria a resposta de erro e prenderia thread do servidor.
                QueueLimit = 0
            });

    private static string PartitionKey(HttpContext context, string policy)
    {
        // A chave inclui o nome da política para que as cotas de credencial e de renovação
        // não se misturem: o mesmo IP tem um balde para cada. Login e cadastro dividem o
        // mesmo balde de propósito — são as duas portas que aceitam senha, e separá-las
        // daria ao atacante o dobro de tentativas.

        // X-Forwarded-For seria necessário atrás de proxy, mas confiar nesse cabeçalho sem
        // o middleware de forwarded headers configurado é pior: o cliente escolheria a
        // própria partição e o limite deixaria de existir. Quando a API for para trás do
        // Front Door, configure UseForwardedHeaders e este endereço passa a ser o real.
        // O envio de anexo é particionado por usuário, e não por IP: a rota exige
        // autenticação, e por IP um escritório atrás de NAT dividiria uma cota só — uma
        // pessoa anexando prints trancaria os colegas para fora.
        if (policy == Uploads)
        {
            var userId = context.User.FindFirst(OpsDeskClaims.Subject)?.Value;

            if (userId is not null)
            {
                return $"{policy}|{userId}";
            }
        }

        var address = context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

        return $"{policy}|{address}";
    }
}
