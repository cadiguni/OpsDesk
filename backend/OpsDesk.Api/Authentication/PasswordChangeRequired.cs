using OpsDesk.Application.Auth;

namespace OpsDesk.Api.Authentication;

/// <summary>
/// Prende quem ainda usa senha provisória em <c>/api/auth</c>.
///
/// Existe porque o primeiro gestor nasce com uma senha que passou por variável de
/// ambiente, e variável de ambiente aparece em log de deploy, em <c>docker inspect</c> e
/// no histórico do shell de quem instalou. Enquanto a troca não acontecer, a credencial
/// deve servir para exatamente uma coisa: trocar a si mesma.
///
/// É middleware, e não filtro de endpoint, de propósito. Filtro é por rota, e rota nova
/// nasce sem ele — o mesmo erro que a invariante 1 do CLAUDE.md evita no filtro de
/// visibilidade. Aqui o padrão é recusar, e quem quiser abrir exceção precisa dizer.
/// </summary>
public static class PasswordChangeRequired
{
    /// <summary>
    /// O tipo do <c>ProblemDetails</c>, para a interface distinguir esta recusa de uma
    /// falta de permissão comum. Sem ele, o cliente veria só um 403 e mandaria a pessoa
    /// para uma tela de "acesso negado" sem saída.
    /// </summary>
    public const string ProblemType = "https://opsdesk.local/errors/password-change-required";

    /// <summary>
    /// O que continua acessível: autenticação, e nada mais. <c>/logout</c> entra na lista
    /// porque ninguém deve ficar preso numa sessão sem conseguir sair dela.
    /// </summary>
    private const string AllowedPrefix = "/api/auth";

    public static IApplicationBuilder UsePasswordChangeRequired(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!Blocks(context))
            {
                await next(context);

                return;
            }

            await Results
                .Problem(
                    detail: "Troque a senha provisória antes de usar o sistema.",
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Troca de senha obrigatória",
                    type: ProblemType)
                .ExecuteAsync(context);
        });

    private static bool Blocks(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (context.User.FindFirst(OpsDeskClaims.MustChangePassword)?.Value != "true")
        {
            return false;
        }

        return !context.Request.Path.StartsWithSegments(
            AllowedPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
