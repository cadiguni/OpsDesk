using Microsoft.Extensions.Options;
using OpsDesk.Infrastructure.Storage;

namespace OpsDesk.Api.Maintenance;

/// <summary>
/// Configuração que sobe calada e quebra depois.
///
/// <c>Jwt:SigningKey</c>, a connection string e <c>AttachmentStorage</c> já derrubam a
/// subida por <c>ValidateOnStart</c>, porque estão ausentes ou presentes. O que sobra aqui
/// é outra categoria: valores que existem, passam em validação de formato e só revelam o
/// problema quando alguém usa o sistema. Origem de CORS com barra no fim nunca casa e o
/// navegador diz apenas "bloqueado"; raiz de anexo sem permissão de escrita só aparece no
/// primeiro envio de arquivo.
///
/// A regra para entrar nesta lista: o sintoma aparece longe da causa.
/// </summary>
internal static class StartupChecks
{
    public static void ValidateConfiguration(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(StartupChecks));

        ValidateCorsOrigins(app, logger);
        ValidateAttachmentRoot(app, logger);
    }

    /// <summary>
    /// Origens de CORS bem formadas.
    ///
    /// Lista vazia **não** é erro: no perfil <c>web</c> o nginx põe SPA e API na mesma
    /// origem, e aí CORS não entra no caminho. Derrubar a subida quebraria justamente a
    /// topologia mais próxima de produção. Fica o aviso, porque a outra leitura possível —
    /// alguém esqueceu de configurar — dá um SPA que não fala com a API.
    ///
    /// Origem malformada, essa sim, é erro: o middleware de CORS compara a origem como
    /// texto, então <c>http://localhost:3000/</c> com barra no fim, ou <c>localhost:3000</c>
    /// sem esquema, nunca casa com nada. O navegador reporta só um bloqueio genérico, e o
    /// log da API não registra nada — é uma tarde perdida procurando no lugar errado.
    /// </summary>
    private static void ValidateCorsOrigins(WebApplication app, ILogger logger)
    {
        var origins = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        if (origins.Length == 0)
        {
            logger.LogWarning(
                "Cors:AllowedOrigins está vazio. Isto só está correto se o SPA for servido " +
                "na mesma origem da API, como no perfil `web` do compose. Em domínios " +
                "separados, o navegador vai bloquear toda chamada do SPA.");

            return;
        }

        var malformed = origins
            .Where(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.AbsolutePath != "/"
                || origin.EndsWith('/'))
            .ToList();

        if (malformed.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cors:AllowedOrigins tem origem malformada: {string.Join(", ", malformed)}. " +
                "Cada origem precisa de esquema e host, sem caminho e sem barra no fim — " +
                "por exemplo http://localhost:3000.");
        }
    }

    /// <summary>
    /// Raiz dos anexos existe e aceita escrita.
    ///
    /// O caso que motivou isto: volume nomeado do Docker herda o dono do caminho que
    /// existir na imagem, e a API roda como usuário sem privilégio. Com o dono errado, a
    /// aplicação sobe saudável, atende tudo, e morre com
    /// <c>UnauthorizedAccessException</c> no primeiro anexo — que pode ser semanas depois.
    /// Escrever um arquivo de sonda na subida troca isso por uma falha no deploy, com o
    /// caminho no texto do erro.
    /// </summary>
    private static void ValidateAttachmentRoot(WebApplication app, ILogger logger)
    {
        var root = Path.GetFullPath(
            app.Services.GetRequiredService<IOptions<AttachmentStorageOptions>>().Value.RootPath);

        var probe = Path.Combine(root, $".opsdesk-write-probe-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(root);

            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"A raiz dos anexos '{root}' não aceita escrita. Em contêiner, o volume " +
                "precisa pertencer ao usuário da aplicação — ver o mkdir e o chown do " +
                "Dockerfile. Trocar a raiz exige recriar o volume, porque o dono é fixado " +
                "no primeiro uso.", ex);
        }

        logger.LogInformation("Raiz dos anexos pronta em {Root}.", root);
    }
}
