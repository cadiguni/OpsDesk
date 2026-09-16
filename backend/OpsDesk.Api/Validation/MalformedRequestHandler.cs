using Microsoft.AspNetCore.Diagnostics;

namespace OpsDesk.Api.Validation;

/// <summary>
/// Traduz falha de leitura do corpo da requisição em <c>400</c>.
///
/// Sem isto, JSON malformado — corpo truncado, tipo errado num campo, bytes que não são
/// UTF-8 válido — estoura como <see cref="BadHttpRequestException"/>, o middleware de
/// exceção não reconhece e a resposta sai <c>500</c>. O problema não é a resposta errada
/// em si: é que 500 significa "defeito nosso" e alimenta alerta de produção. Cliente
/// mandando corpo inválido não é incidente, é entrada inválida, e a diferença precisa
/// aparecer no monitoramento.
/// </summary>
public class MalformedRequestHandler(ILogger<MalformedRequestHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        // Nível de aviso, não de erro: é o cliente que errou.
        logger.LogWarning(
            "Requisição malformada em {Method} {Path}: {Reason}",
            context.Request.Method,
            context.Request.Path,
            badRequest.Message);

        context.Response.StatusCode = badRequest.StatusCode;

        await context.Response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                title = "Requisição inválida",
                status = badRequest.StatusCode,

                // A mensagem da exceção pode conter detalhe do parser; devolvemos algo
                // estável e sem eco do que o cliente mandou.
                detail = "Não foi possível interpretar o corpo da requisição.",
            },
            cancellationToken);

        return true;
    }
}
