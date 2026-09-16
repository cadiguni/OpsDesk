using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Auth;

/// <summary>
/// Cadastro, login, renovação e encerramento de sessão.
///
/// O acesso a dados é LINQ direto sobre o contexto, sem repositório genérico no meio:
/// decisão 4.9 de docs/arquitetura.md.
/// </summary>
public class AuthService(
    IOpsDeskDbContext db,
    IPasswordHasher<User> passwordHasher,
    IAccessTokenService accessTokens,
    IRefreshTokenStore refreshTokens)
{
    /// <summary>
    /// Hash descartável usado quando o e-mail não existe.
    ///
    /// Sem isto, login com e-mail inexistente responde na hora e login com e-mail válido
    /// e senha errada demora o tempo do PBKDF2 — diferença medível, que transforma o
    /// endpoint em um oráculo de "esta pessoa tem conta aqui". Verificando contra um hash
    /// fixo, os dois caminhos custam o mesmo.
    /// </summary>
    private static readonly string DummyHash =
        new PasswordHasher<User>().HashPassword(new User(), "senha-que-nao-existe");

    public async Task<AuthResult> RegisterAsync(
        RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = Normalize(request.Email);

        if (await Users().AnyAsync(u => u.Email == email, cancellationToken))
        {
            return new AuthResult.EmailAlreadyUsed();
        }

        var user = new User
        {
            Name = request.Name.Trim(),
            Email = email,
            // README, seção 13.2: quem se cadastra pelo portal nasce solicitante.
            // Técnico e gestor são promovidos fora do fluxo público.
            Role = UserRole.Requester
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Duas requisições com o mesmo e-mail podem passar pela verificação acima e
            // colidir no índice único. Quem chega depois recebe o mesmo erro de negócio
            // que receberia se tivesse chegado um instante mais tarde.
            return new AuthResult.EmailAlreadyUsed();
        }

        return await IssueSessionAsync(user, cancellationToken);
    }

    public async Task<AuthResult> LoginAsync(
        LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = Normalize(request.Email);

        var user = await Users().SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Só é candidato a autenticar quem existe, está ativo e tem senha definida. Conta
        // sem senha é o solicitante criado a partir de um e-mail recebido, a partir da
        // versão 2.0: é dono de chamados, mas ainda não escolheu senha.
        if (user is { IsActive: true, PasswordHash: { } passwordHash })
        {
            var verification = passwordHasher.VerifyHashedPassword(
                user, passwordHash, request.Password);

            if (verification == PasswordVerificationResult.Failed)
            {
                return Reject(request.Password, alreadyCompared: true);
            }

            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                // O PasswordHasher mudou de parâmetros desde o cadastro. Reescrevemos o
                // hash no único momento em que a senha em claro está disponível.
                user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
                await db.SaveChangesAsync(cancellationToken);
            }

            return await IssueSessionAsync(user, cancellationToken);
        }

        return Reject(request.Password, alreadyCompared: false);
    }

    /// <summary>
    /// Recusa de login, sempre com a mesma resposta.
    ///
    /// E-mail inexistente, senha errada, conta desativada e conta sem senha devolvem
    /// exatamente o mesmo status e a mesma mensagem, de propósito. Responder "conta
    /// desativada" seria mais gentil com quem esqueceu a senha e também confirmaria, para
    /// quem estivesse testando endereços, que aquela pessoa tem conta aqui. Como o resto
    /// do login foi desenhado para não vazar isso, abrir a exceção justo aqui tornaria
    /// toda a precaução inútil.
    ///
    /// Quem precisa definir senha chega lá pelo fluxo próprio, por link enviado ao
    /// e-mail, não por uma dica na tela de login.
    /// </summary>
    /// <param name="alreadyCompared">
    /// Se a comparação de senha já aconteceu. Quando não aconteceu — e-mail inexistente,
    /// conta desativada, conta sem senha — comparamos contra um hash descartável só para
    /// gastar o mesmo tempo. Sem isso, a diferença de latência entre os caminhos
    /// responderia "esta pessoa tem conta aqui" para quem medisse.
    /// </param>
    private AuthResult Reject(string attemptedPassword, bool alreadyCompared)
    {
        if (!alreadyCompared)
        {
            _ = passwordHasher.VerifyHashedPassword(new User(), DummyHash, attemptedPassword);
        }

        return new AuthResult.InvalidCredentials();
    }

    public async Task<AuthResult> RefreshAsync(
        string? presentedToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return new AuthResult.InvalidRefreshToken();
        }

        var rotation = await refreshTokens.RotateAsync(presentedToken, cancellationToken);

        if (rotation is not RefreshTokenRotation.Rotated rotated)
        {
            return new AuthResult.InvalidRefreshToken();
        }

        var user = await Users().SingleOrDefaultAsync(u => u.Id == rotated.UserId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            // Conta desativada durante a sessão: a renovação para aqui, e as demais
            // sessões dela caem junto.
            await refreshTokens.RevokeAllForUserAsync(rotated.UserId, cancellationToken);

            return new AuthResult.InvalidRefreshToken();
        }

        var access = accessTokens.Create(user);

        return new AuthResult.Success(
            access.Value,
            access.ExpiresAt,
            rotated.Token.Value,
            rotated.Token.ExpiresAt,
            ToSessionUser(user));
    }

    public Task LogoutAsync(string? presentedToken, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(presentedToken)
            ? Task.CompletedTask
            : refreshTokens.RevokeAsync(presentedToken, cancellationToken);

    /// <summary>Usuário da sessão atual, lido do banco e não das claims.</summary>
    public async Task<SessionUser?> GetSessionUserAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        await Users()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new SessionUser(u.Id, u.Name, u.Email, u.Role))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<AuthResult> IssueSessionAsync(User user, CancellationToken cancellationToken)
    {
        var access = accessTokens.Create(user);
        var refresh = await refreshTokens.IssueAsync(user.Id, cancellationToken);

        return new AuthResult.Success(
            access.Value, access.ExpiresAt, refresh.Value, refresh.ExpiresAt, ToSessionUser(user));
    }

    private IQueryable<User> Users() => db.Users;

    private static SessionUser ToSessionUser(User user) =>
        new(user.Id, user.Name, user.Email, user.Role);

    /// <summary>
    /// E-mail é identificador de login: comparamos sempre em minúsculas e sem espaço nas
    /// pontas, para que "Joao@Empresa.com" e "joao@empresa.com" não virem duas contas.
    /// </summary>
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
