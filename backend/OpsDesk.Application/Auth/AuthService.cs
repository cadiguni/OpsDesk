using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Settings;
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

        // A política vem antes de olhar se o e-mail existe: com o cadastro fechado, nem
        // "esse e-mail já tem conta" deve ser respondido a quem não poderia se cadastrar.
        var policy = await db.SystemSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        if (policy is { AllowSelfRegistration: false })
        {
            return new AuthResult.RegistrationClosed();
        }

        if (!SystemSettingsService.AcceptsEmail(policy, email))
        {
            return new AuthResult.EmailDomainNotAllowed(SystemSettingsService.DomainsOf(policy));
        }

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

    /// <summary>
    /// Abre a sessão de quem o Entra ID autenticou, criando a conta no primeiro acesso.
    ///
    /// A pessoa é reconhecida pelo <c>oid</c> e, na falta dele, pelo e-mail — é assim que a
    /// conta que já existia, com senha ou criada por um e-mail recebido, passa a entrar pela
    /// Microsoft sem duplicar e sem perder perfil nem chamados. O vínculo pelo e-mail só é
    /// seguro porque o login aceita um único tenant, onde quem define o e-mail é o
    /// administrador da empresa (ver <see cref="MicrosoftSignInSettings.IsSingleTenant"/>).
    ///
    /// Conta criada aqui nasce solicitante e sem senha, como a do autocadastro; o perfil de
    /// equipe continua sendo dado pelo gestor.
    /// </summary>
    public async Task<AuthResult> SignInWithMicrosoftAsync(
        EntraIdentity identity, CancellationToken cancellationToken = default)
    {
        var email = Normalize(identity.Email);

        var user = await Users().SingleOrDefaultAsync(u => u.EntraObjectId == identity.ObjectId, cancellationToken)
                   ?? await Users().SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Name = string.IsNullOrWhiteSpace(identity.Name) ? email[..email.IndexOf('@')] : identity.Name.Trim(),
                Email = email,
                Role = UserRole.Requester,
                EntraObjectId = identity.ObjectId
            };

            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Dois primeiros logins simultâneos — duas abas, dois cliques. Quem perdeu a
                // corrida encontra a conta que o outro criou e segue por ela.
                db.Users.Remove(user);

                user = await Users().SingleOrDefaultAsync(u => u.EntraObjectId == identity.ObjectId, cancellationToken);

                if (user is null)
                {
                    return new AuthResult.MicrosoftAccountRejected("conflito");
                }
            }
        }
        else if (user.EntraObjectId is null)
        {
            user.EntraObjectId = identity.ObjectId;
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (user.EntraObjectId != identity.ObjectId)
        {
            // O e-mail é desta conta, mas ela já entra por outra identidade do Entra ID —
            // conta recriada no tenant, ou e-mail reaproveitado para outra pessoa. Assumir
            // a conta pelo e-mail entregaria os chamados de uma pessoa a outra.
            return new AuthResult.MicrosoftAccountRejected("conflito");
        }

        if (!user.IsActive)
        {
            return new AuthResult.MicrosoftAccountRejected("desativada");
        }

        return await IssueSessionAsync(user, cancellationToken);
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

    /// <summary>
    /// Troca a senha do próprio usuário e reabre a sessão.
    ///
    /// As demais sessões caem: trocar a senha é o que a pessoa faz quando desconfia que
    /// alguém tem a credencial, e deixar refresh token antigo válido esvaziaria o gesto.
    /// A sessão de quem trocou é reemitida na mesma resposta, para a interface não
    /// devolver a pessoa ao login logo depois de ela ter provado quem é.
    /// </summary>
    public async Task<AuthResult> ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await Users().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is not { IsActive: true, PasswordHash: { } passwordHash })
        {
            return new AuthResult.InvalidCredentials();
        }

        if (passwordHasher.VerifyHashedPassword(user, passwordHash, request.CurrentPassword)
            == PasswordVerificationResult.Failed)
        {
            return new AuthResult.InvalidCredentials();
        }

        // Comparada contra o hash, e não texto com texto: a senha atual chega em claro,
        // mas o que temos guardado da antiga é o hash, e é ele que decide.
        if (passwordHasher.VerifyHashedPassword(user, passwordHash, request.NewPassword)
            != PasswordVerificationResult.Failed)
        {
            return new AuthResult.PasswordUnchanged();
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.PasswordChangeReason = UserAuditAction.PasswordChanged;

        // A senha agora é escolha da pessoa, e não mais um valor que passou por
        // configuração. A trava sai.
        user.MustChangePassword = false;

        await db.SaveChangesAsync(cancellationToken);

        await refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);

        return await IssueSessionAsync(user, cancellationToken);
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
            .Select(u => new SessionUser(u.Id, u.Name, u.Email, u.Role, u.MustChangePassword))
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
        new(user.Id, user.Name, user.Email, user.Role, user.MustChangePassword);

    /// <summary>
    /// E-mail é identificador de login: comparamos sempre em minúsculas e sem espaço nas
    /// pontas, para que "Joao@Empresa.com" e "joao@empresa.com" não virem duas contas.
    /// </summary>
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
