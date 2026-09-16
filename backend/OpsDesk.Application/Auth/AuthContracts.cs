using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Auth;

/// <summary>Dados de cadastro pelo portal. Todo usuário criado aqui nasce solicitante.</summary>
public record RegisterRequest(string Name, string Email, string Password, string PasswordConfirmation);

public record LoginRequest(string Email, string Password);

/// <summary>Usuário autenticado, no formato que a interface consome.</summary>
public record SessionUser(Guid Id, string Name, string Email, UserRole Role);

/// <summary>
/// Resultado de uma operação de autenticação.
///
/// É um tipo de retorno, não exceção: credencial errada é fluxo esperado de um endpoint
/// de login, não condição excepcional. Exceção para isso encheria o log de ruído e
/// esconderia as falhas que realmente importam.
/// </summary>
public abstract record AuthResult
{
    /// <summary>
    /// Autenticação concluída. O <paramref name="RefreshToken"/> vem em claro porque a
    /// API precisa colocá-lo no cookie; no banco fica apenas o hash.
    /// </summary>
    public sealed record Success(
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAt,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt,
        SessionUser User) : AuthResult;

    /// <summary>
    /// Credencial recusada. A mensagem é deliberadamente genérica: distinguir
    /// "e-mail não existe" de "senha errada" entrega a lista de usuários do sistema
    /// para quem estiver testando endereços.
    /// </summary>
    public sealed record InvalidCredentials : AuthResult
    {
        public string Message => "E-mail ou senha inválidos.";
    }

    /// <summary>E-mail já cadastrado.</summary>
    public sealed record EmailAlreadyUsed : AuthResult
    {
        public string Message => "Já existe uma conta com este e-mail.";
    }

    /// <summary>Refresh token ausente, expirado, revogado ou desconhecido.</summary>
    public sealed record InvalidRefreshToken : AuthResult
    {
        public string Message => "Sessão expirada. Entre novamente.";
    }
}
