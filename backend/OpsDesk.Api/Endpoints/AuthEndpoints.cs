using OpsDesk.Api.Authentication;
using OpsDesk.Api.RateLimiting;
using OpsDesk.Api.Validation;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Auth;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// Endpoints de autenticação.
///
/// O refresh token nunca aparece no corpo de uma resposta — ele existe só no cookie
/// <c>httpOnly</c>. Devolvê-lo em JSON anularia a razão de usar o cookie, porque
/// voltaria a ser legível por JavaScript.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Token de acesso e usuário da sessão. Sem refresh token, de propósito.</summary>
    public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, SessionUser User);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/auth")
            .WithTags("Autenticação");

        group.MapPost("/register", Register)
            .ValidatingBody<RegisterRequest>()
            .RequireRateLimiting(RateLimitPolicies.Anonymous)
            .AllowAnonymous()
            .WithSummary("Cria uma conta de solicitante e abre a sessão.");

        group.MapPost("/login", Login)
            .ValidatingBody<LoginRequest>()
            .RequireRateLimiting(RateLimitPolicies.Anonymous)
            .AllowAnonymous()
            .WithSummary("Autentica e abre a sessão.");

        group.MapPost("/refresh", Refresh)
            .RequireRateLimiting(RateLimitPolicies.Anonymous)
            .AllowAnonymous()
            .WithSummary("Renova o token de acesso a partir do cookie de refresh.");

        group.MapPost("/logout", Logout)
            .AllowAnonymous()
            .WithSummary("Encerra a sessão e revoga o refresh token.");

        group.MapGet("/me", Me)
            .RequireAuthorization()
            .WithSummary("Usuário da sessão atual.");

        return routes;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        AuthService auth,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await auth.RegisterAsync(request, cancellationToken);

        return Respond(result, response);
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        AuthService auth,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, cancellationToken);

        return Respond(result, response);
    }

    private static async Task<IResult> Refresh(
        AuthService auth,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await auth.RefreshAsync(RefreshTokenCookie.Read(request), cancellationToken);

        return Respond(result, response);
    }

    private static async Task<IResult> Logout(
        AuthService auth,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(RefreshTokenCookie.Read(request), cancellationToken);

        // O cookie sai mesmo que o token já não valesse: o cliente não pode terminar um
        // logout ainda carregando credencial.
        RefreshTokenCookie.Delete(response);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> Me(
        AuthService auth,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        // Lido do banco, não das claims: o token dura quinze minutos, e nesse intervalo o
        // perfil ou o nome podem ter mudado. Quem pergunta "quem sou eu" quer o estado
        // atual, não a foto do momento do login.
        var user = await auth.GetSessionUserAsync(userId, cancellationToken);

        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(user);
    }

    private static IResult Respond(AuthResult result, HttpResponse response)
    {
        switch (result)
        {
            case AuthResult.Success success:
                RefreshTokenCookie.Write(response, success.RefreshToken, success.RefreshTokenExpiresAt);

                return TypedResults.Ok(new AuthResponse(
                    success.AccessToken, success.AccessTokenExpiresAt, success.User));

            case AuthResult.EmailAlreadyUsed conflict:
                return TypedResults.Problem(
                    detail: conflict.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "E-mail já cadastrado");

            case AuthResult.InvalidCredentials invalid:
                return TypedResults.Problem(
                    detail: invalid.Message,
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Credenciais inválidas");

            case AuthResult.InvalidRefreshToken expired:
                // A sessão acabou: limpa o cookie para o cliente não insistir em um token
                // que já foi recusado.
                RefreshTokenCookie.Delete(response);

                return TypedResults.Problem(
                    detail: expired.Message,
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Sessão expirada");

            default:
                throw new InvalidOperationException(
                    $"Resultado de autenticação não tratado: {result.GetType().Name}.");
        }
    }
}
