using FluentValidation;

namespace OpsDesk.Api.Validation;

/// <summary>
/// Roda o validator do FluentValidation antes do handler e devolve
/// <c>400 ValidationProblem</c> quando o corpo não passa.
///
/// É um filtro de endpoint, e não uma verificação dentro de cada handler, para que
/// adicionar um endpoint novo com validação seja uma linha — e para que ninguém consiga
/// esquecer de chamar o validator.
/// </summary>
public class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<TRequest>().FirstOrDefault() is not { } request)
        {
            return TypedResults.Problem(
                title: "Corpo da requisição ausente.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        return result.IsValid
            ? await next(context)
            : TypedResults.ValidationProblem(result.ToDictionary());
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>Liga a validação do corpo ao endpoint.</summary>
    public static RouteHandlerBuilder ValidatingBody<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class =>
        builder
            .AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem();
}
