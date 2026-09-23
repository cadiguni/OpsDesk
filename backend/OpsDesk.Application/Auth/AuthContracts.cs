using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Auth;

/// <summary>Dados de cadastro pelo portal. Todo usuário criado aqui nasce solicitante.</summary>
public record RegisterRequest(string Name, string Email, string Password, string PasswordConfirmation);

public record LoginRequest(string Email, string Password);

/// <summary>
/// Troca de senha pelo próprio usuário.
///
/// A senha atual é exigida mesmo havendo sessão válida: o token de acesso pode estar numa
/// máquina deixada aberta, e trocar a senha é justamente a operação que fecha as outras
/// sessões. Sem a senha atual, quem senta na cadeira de outra pessoa assume a conta.
/// </summary>
public record ChangePasswordRequest(
    string CurrentPassword, string NewPassword, string NewPasswordConfirmation);

/// <summary>
/// Usuário autenticado, no formato que a interface consome.
/// </summary>
/// <param name="MustChangePassword">
/// A senha atual foi entregue por um canal que não a mantém secreta — hoje, a variável de
/// ambiente do bootstrap do primeiro gestor. Enquanto for verdadeiro, a API recusa tudo
/// fora de <c>/api/auth</c> e a interface prende a navegação na tela de troca.
/// </param>
public record SessionUser(
    Guid Id, string Name, string Email, UserRole Role, bool MustChangePassword);

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

    /// <summary>
    /// A nova senha não pode ser igual à atual.
    ///
    /// Importa sobretudo na troca obrigatória: repetir a senha do bootstrap deixaria em
    /// pé exatamente a credencial que a troca existe para aposentar.
    /// </summary>
    public sealed record PasswordUnchanged : AuthResult
    {
        public string Message => "A nova senha precisa ser diferente da atual.";
    }

    /// <summary>Refresh token ausente, expirado, revogado ou desconhecido.</summary>
    public sealed record InvalidRefreshToken : AuthResult
    {
        public string Message => "Sessão expirada. Entre novamente.";
    }
}
