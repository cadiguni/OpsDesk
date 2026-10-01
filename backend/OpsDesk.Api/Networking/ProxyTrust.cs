using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace OpsDesk.Api.Networking;

/// <summary>
/// Em quais proxies a API confia para ler o IP de origem do cabeçalho
/// <c>X-Forwarded-For</c>.
///
/// Atrás do nginx do SPA — e do Caddy, no compose de produção — toda requisição chega à
/// API com o IP do proxy. O rate limiting de login particiona por IP, então sem isto a
/// cota de tentativas vira uma só para a empresa inteira: alguém errando a senha em loop
/// bloqueia o login de todo mundo.
///
/// Confiar no cabeçalho de qualquer origem seria pior que não ler: o cliente escolheria a
/// própria partição e a cota deixaria de existir. Por isso a lista é explícita — a rede
/// interna do compose, ou o endereço do balanceador — e vazia por padrão, quando só o
/// loopback é confiável.
/// </summary>
public static class ProxyTrust
{
    public const string SectionName = "ForwardedHeaders";

    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Quantos saltos ler da direita para a esquerda. Caddy → nginx → API são dois.
        options.ForwardLimit = section.GetValue<int?>("ForwardLimit") ?? 1;

        foreach (var cidr in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
        {
            if (!System.Net.IPNetwork.TryParse(cidr, out var network))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:KnownNetworks tem uma rede inválida: '{cidr}'. Use notação CIDR, como 172.28.0.0/16.");
            }

            options.KnownIPNetworks.Add(network);
        }

        foreach (var address in section.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            if (!IPAddress.TryParse(address, out var proxy))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:KnownProxies tem um endereço inválido: '{address}'.");
            }

            options.KnownProxies.Add(proxy);
        }
    }
}
