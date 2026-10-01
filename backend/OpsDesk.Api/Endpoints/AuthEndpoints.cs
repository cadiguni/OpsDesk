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
            .RequireRateLimiting(RateLimitPolicies.Credentials)
            .AllowAnonymous()
            .WithSummary("Cria uma conta de solicitante e abre a sessão.");

        group.MapPost("/login", Login)
            .ValidatingBody<LoginRequest>()
            .RequireRateLimiting(RateLimitPolicies.Credentials)
            .AllowAnonymous()
            .WithSummary("Autentica e abre a sessão.");

        group.MapPost("/refresh", Refresh)
            .RequireRateLimiting(RateLimitPolicies.Refresh)
            .AllowAnonymous()
            .WithSummary("Renova o token de acesso a partir do cookie de refresh.");

        group.MapPost("/logout", Logout)
            .AllowAnonymous()
            .WithSummary("Encerra a sessão e revoga o refresh token.");

        group.MapPost("/password", ChangePassword)
            .ValidatingBody<ChangePasswordRequest>()
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithSummary("Troca a senha do usuário da sessão e reabre a sessão.");

        group.MapGet("/options", Options)
            .AllowAnonymous()
            .WithSummary("O que a tela de login oferece: autocadastro, domínios aceitos, redefinição por e-mail.");

        group.MapPost("/password/forgot", ForgotPassword)
            .ValidatingBody<ForgotPasswordRequest>()
            .RequireRateLimiting(RateLimitPolicies.Credentials)
            .AllowAnonymous()
            .WithSummary("Pede o link de redefinição de senha. Responde igual exista ou não a conta.");

        group.MapPost("/password/reset", ResetPassword)
            .ValidatingBody<ResetPasswordRequest>()
            .RequireRateLimiting(RateLimitPolicies.Credentials)
            .AllowAnonymous()
            .WithSummary("Define a senha pelo link recebido por e-mail.");

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

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request,
        AuthService auth,
        ICurrentUser currentUser,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await auth.ChangePasswordAsync(userId, request, cancellationToken);

        return Respond(result, response);
    }

    private static async Task<IResult> Options(
        Application.Settings.SystemSettingsService settings, CancellationToken cancellationToken) =>
        TypedResults.Ok(await settings.GetPublicAsync(cancellationToken));

    private static async Task<IResult> ForgotPassword(
        ForgotPasswordRequest request, PasswordResetService reset, CancellationToken cancellationToken)
    {
        await reset.RequestAsync(request, cancellationToken);

        // 202 sempre: a resposta não pode dizer se o e-mail tem conta.
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> ResetPassword(
        ResetPasswordRequest request, PasswordResetService reset, CancellationToken cancellationToken) =>
        await reset.ResetAsync(request, cancellationToken) == ResetPasswordResult.Reset
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                detail: "Este link não vale mais: ele já foi usado, expirou ou foi substituído por um pedido mais novo. Peça outro.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Link inválido");

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

            case AuthResult.PasswordUnchanged unchanged:
                return TypedResults.Problem(
                    detail: unchanged.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Senha inalterada");

            case AuthResult.RegistrationClosed closed:
                return TypedResults.Problem(
                    detail: closed.Message,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Cadastro desligado");

            case AuthResult.EmailDomainNotAllowed domain:
                return TypedResults.Problem(
                    detail: domain.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Domínio de e-mail não aceito");

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
