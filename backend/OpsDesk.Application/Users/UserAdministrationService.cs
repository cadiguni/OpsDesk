using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Authorization;
using OpsDesk.Application.Common;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Domain.Tickets;

namespace OpsDesk.Application.Users;

/// <summary>
/// Administração de usuários pelo administrador: listar, cadastrar, trocar perfil e cargo,
/// desativar e reativar.
///
/// O acesso é restrito a administrador pela política da rota, e a verificação se repete aqui
/// pelo mesmo motivo do <c>AssignAsync</c>: a política barra o endpoint, mas é o serviço
/// que conhece a regra, e serviço chamado de outro lugar não herda atributo de rota.
///
/// Sobre sessões: o perfil viaja no token de acesso. Rebaixar ou desativar revoga todos
/// os refresh tokens da pessoa, para que a próxima renovação falhe; o token de acesso já
/// emitido continua valendo até expirar (<c>Jwt:AccessTokenMinutes</c>). Promover não
/// revoga nada — a renovação seguinte já lê o perfil novo do banco.
/// </summary>
public class UserAdministrationService(
    IOpsDeskDbContext db,
    IRefreshTokenStore refreshTokens,
    PasswordResetService passwordReset,
    IPasswordHasher<User> passwordHasher,
    ILogger<UserAdministrationService> logger)
{
    /// <summary>
    /// Cadastra uma pessoa (README, seção 13.9). Com e-mail disponível, ela recebe um
    /// convite para definir a senha; sem, o administrador recebe uma senha provisória.
    /// </summary>
    public async Task<ChangeUserResult> CreateAsync(
        CreateUserRequest request, TicketViewer actor, CancellationToken cancellationToken = default)
    {
        if (!await IsActiveManagerAsync(actor, cancellationToken))
        {
            return new ChangeUserResult.NotAllowed();
        }

        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return new ChangeUserResult.EmailAlreadyUsed();
        }

        if (request.JobTitleId is { } jobTitleId && !await IsSelectableJobTitleAsync(jobTitleId, cancellationToken))
        {
            return new ChangeUserResult.JobTitleUnavailable();
        }

        var user = new User
        {
            Name = request.Name.Trim(), Email = email, Role = request.Role, JobTitleId = request.JobTitleId
        };
        db.Users.Add(user);

        string? temporaryPassword = null;
        var invited = await passwordReset.InviteAsync(user, cancellationToken);

        if (!invited)
        {
            temporaryPassword = TemporaryPassword.Generate();
            user.PasswordHash = passwordHasher.HashPassword(user, temporaryPassword);
            user.MustChangePassword = true;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return new ChangeUserResult.EmailAlreadyUsed();
        }

        logger.LogInformation(
            "Usuário {UserId} cadastrado por {ActorId} como {Role} ({Access}).",
            user.Id, actor.UserId, user.Role, invited ? "convite por e-mail" : "senha provisória");

        return new ChangeUserResult.AccessIssued(
            new AccessGranted(await ProjectOneAsync(user.Id, cancellationToken), invited, temporaryPassword));
    }

    /// <summary>
    /// Redefine a senha de outra pessoa: mesmo critério do cadastro — link por e-mail quando
    /// houver, senha provisória quando não. As sessões abertas dela caem nos dois casos.
    /// </summary>
    public async Task<ChangeUserResult> ResetPasswordAsync(
        Guid userId, TicketViewer actor, CancellationToken cancellationToken = default)
    {
        var (user, refusal) = await LoadAsync(userId, actor, cancellationToken);

        if (user is null)
        {
            return refusal!;
        }

        string? temporaryPassword = null;
        var invited = user.IsActive && await passwordReset.InviteAsync(user, cancellationToken);

        if (invited)
        {
            // A senha atual morre agora, e não quando a pessoa usar o link: redefinir por
            // suspeita de conta invadida não pode deixar a senha do intruso valendo até lá.
            user.PasswordHash = null;
        }
        else
        {
            temporaryPassword = TemporaryPassword.Generate();
            user.PasswordHash = passwordHasher.HashPassword(user, temporaryPassword);
            user.MustChangePassword = true;
        }

        user.PasswordChangeReason = UserAuditAction.PasswordReset;

        await db.SaveChangesAsync(cancellationToken);
        await refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);

        logger.LogInformation("Senha de {UserId} redefinida por {ActorId}.", user.Id, actor.UserId);

        return new ChangeUserResult.AccessIssued(
            new AccessGranted(await ProjectOneAsync(user.Id, cancellationToken), invited, temporaryPassword));
    }

    /// <summary>Trilha de auditoria da pessoa, mais recente primeiro.</summary>
    public async Task<List<UserAuditItem>?> HistoryAsync(
        Guid userId, TicketViewer actor, CancellationToken cancellationToken = default)
    {
        if (!await IsActiveManagerAsync(actor, cancellationToken)
            || !await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
        {
            return null;
        }

        return await db.UserAudit
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .Select(a => new UserAuditItem(
                a.Action, a.PreviousValue, a.NewValue, a.ChangedBy == null ? null : a.ChangedBy.Name, a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Troca o cargo. Diferente de perfil e ativação, vale também para a própria conta:
    /// cargo não dá permissão, então não há como alguém se trancar fora mexendo nele.
    /// </summary>
    public async Task<ChangeUserResult> ChangeJobTitleAsync(
        Guid userId,
        ChangeJobTitleRequest request,
        TicketViewer actor,
        CancellationToken cancellationToken = default)
    {
        if (!await IsActiveManagerAsync(actor, cancellationToken))
        {
            return new ChangeUserResult.NotAllowed();
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return new ChangeUserResult.UserNotFound();
        }

        if (user.JobTitleId == request.JobTitleId)
        {
            return await ChangedAsync(user.Id, cancellationToken);
        }

        if (request.JobTitleId is { } jobTitleId && !await IsSelectableJobTitleAsync(jobTitleId, cancellationToken))
        {
            return new ChangeUserResult.JobTitleUnavailable();
        }

        // O antigo e o novo ficam no rastreamento para a trilha de auditoria gravar os
        // nomes sem ir ao banco de dentro do interceptor.
        await db.JobTitles
            .Where(j => j.Id == user.JobTitleId || j.Id == request.JobTitleId)
            .LoadAsync(cancellationToken);

        var previous = user.JobTitleId;
        user.JobTitleId = request.JobTitleId;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Cargo de {UserId} alterado de {PreviousJobTitle} para {NewJobTitle} por {ActorId}.",
            user.Id, previous, request.JobTitleId, actor.UserId);

        return await ChangedAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// Troca o nome exibido. Como o cargo, vale também para a própria conta: o nome não dá
    /// permissão. É o caminho para corrigir o nome do administrador do bootstrap, que nasce
    /// com um nome genérico.
    /// </summary>
    public async Task<ChangeUserResult> ChangeNameAsync(
        Guid userId,
        ChangeNameRequest request,
        TicketViewer actor,
        CancellationToken cancellationToken = default)
    {
        if (!await IsActiveManagerAsync(actor, cancellationToken))
        {
            return new ChangeUserResult.NotAllowed();
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return new ChangeUserResult.UserNotFound();
        }

        var name = request.Name.Trim();

        if (user.Name != name)
        {
            user.Name = name;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Nome de {UserId} alterado por {ActorId}.", user.Id, actor.UserId);
        }

        return await ChangedAsync(user.Id, cancellationToken);
    }

    private Task<bool> IsSelectableJobTitleAsync(Guid jobTitleId, CancellationToken cancellationToken) =>
        db.JobTitles.AnyAsync(j => j.Id == jobTitleId && j.IsActive, cancellationToken);

    private Task<ManagedUser> ProjectOneAsync(Guid userId, CancellationToken cancellationToken) =>
        Project(db.Users.AsNoTracking().Where(u => u.Id == userId)).SingleAsync(cancellationToken);

    private async Task<bool> IsActiveManagerAsync(TicketViewer actor, CancellationToken cancellationToken) =>
        actor.IsManager
        && await db.Users.AnyAsync(u => u.Id == actor.UserId && u.IsActive && u.Role == UserRole.Manager, cancellationToken);

    public Task<PagedResult<ManagedUser>> ListAsync(
        ManagedUserFilter filter, CancellationToken cancellationToken = default)
    {
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();

            // Mesmo critério do UserDirectoryService: `ToLower()` nos dois lados, para não
            // amarrar a Application ao `ILIKE` do Npgsql.
            query = query.Where(u =>
                u.Name.ToLower().Contains(term) || u.Email.ToLower().Contains(term));
        }

        if (filter.Role is { } role)
        {
            query = query.Where(u => u.Role == role);
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(u => u.IsActive == isActive);
        }

        return Project(query.OrderBy(u => u.Name).ThenBy(u => u.Email))
            .ToPagedResultAsync(filter.Page, cancellationToken);
    }

    public async Task<ChangeUserResult> ChangeRoleAsync(
        Guid userId,
        ChangeRoleRequest request,
        TicketViewer actor,
        CancellationToken cancellationToken = default)
    {
        var (user, refusal) = await LoadAsync(userId, actor, cancellationToken);

        if (user is null)
        {
            return refusal!;
        }

        if (user.Role == request.Role)
        {
            return await ChangedAsync(user.Id, cancellationToken);
        }

        // Rebaixar a solicitante tira a pessoa da equipe, e chamado atribuído a quem não é
        // da equipe é chamado que nenhum técnico vê como seu. Técnico ↔ administrador não mexe
        // nisso: os dois atendem.
        if (request.Role == UserRole.Requester)
        {
            var blocking = await OpenAssignedTicketsAsync(user.Id, cancellationToken);

            if (blocking.Count > 0)
            {
                return new ChangeUserResult.HasOpenAssignedTickets(blocking);
            }
        }

        var previous = user.Role;
        user.Role = request.Role;

        await db.SaveChangesAsync(cancellationToken);

        if (IsDemotion(previous, request.Role))
        {
            await refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);
        }

        logger.LogInformation(
            "Perfil de {UserId} alterado de {PreviousRole} para {NewRole} por {ActorId}.",
            user.Id, previous, request.Role, actor.UserId);

        return await ChangedAsync(user.Id, cancellationToken);
    }

    public async Task<ChangeUserResult> ChangeActivationAsync(
        Guid userId,
        ChangeActivationRequest request,
        TicketViewer actor,
        CancellationToken cancellationToken = default)
    {
        var (user, refusal) = await LoadAsync(userId, actor, cancellationToken);

        if (user is null)
        {
            return refusal!;
        }

        if (user.IsActive == request.IsActive)
        {
            return await ChangedAsync(user.Id, cancellationToken);
        }

        if (!request.IsActive)
        {
            var blocking = await OpenAssignedTicketsAsync(user.Id, cancellationToken);

            if (blocking.Count > 0)
            {
                return new ChangeUserResult.HasOpenAssignedTickets(blocking);
            }
        }

        user.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        // Desativar derruba as sessões agora, e não na próxima renovação: o RefreshAsync
        // já recusaria conta inativa, mas revogar aqui deixa o banco dizendo a verdade
        // para quem for investigar.
        if (!request.IsActive)
        {
            await refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);
        }

        logger.LogInformation(
            "Conta de {UserId} {Action} por {ActorId}.",
            user.Id, request.IsActive ? "reativada" : "desativada", actor.UserId);

        return await ChangedAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// Confere que quem age é administrador ativo, e carrega o alvo.
    ///
    /// O perfil de quem age é conferido no banco, e não só na claim. O token de acesso
    /// carrega o perfil do momento em que foi emitido, e um administrador recém-rebaixado ou
    /// desativado seguiria com ele por até <c>Jwt:AccessTokenMinutes</c>. Para ler chamado
    /// essa janela é aceitável; para promover alguém a administrador, não.
    /// </summary>
    private async Task<(User? Target, ChangeUserResult? Refusal)> LoadAsync(
        Guid userId, TicketViewer actor, CancellationToken cancellationToken)
    {
        var actorIsManager = actor.IsManager && await db.Users.AnyAsync(
            u => u.Id == actor.UserId && u.IsActive && u.Role == UserRole.Manager,
            cancellationToken);

        if (!actorIsManager)
        {
            return (null, new ChangeUserResult.NotAllowed());
        }

        var target = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (target is null)
        {
            return (null, new ChangeUserResult.UserNotFound());
        }

        return target.Id == actor.UserId
            ? (null, new ChangeUserResult.SelfChangeNotAllowed())
            : (target, null);
    }

    /// <summary>
    /// Chamados não terminais sob responsabilidade da pessoa. "Resolvido" conta: o
    /// solicitante ainda pode devolvê-lo para atendimento.
    /// </summary>
    private Task<List<BlockingTicket>> OpenAssignedTicketsAsync(
        Guid userId, CancellationToken cancellationToken) =>
        db.Tickets
            .AsNoTracking()
            .Where(t => t.AssignedTechnicianId == userId
                        && !TicketStatusMachine.Finished.Contains(t.Status))
            .OrderBy(t => t.CreatedAt)
            .Select(t => new BlockingTicket(t.Id, t.Code, t.Title, t.Status))
            .ToListAsync(cancellationToken);

    private async Task<ChangeUserResult> ChangedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await Project(db.Users.AsNoTracking().Where(u => u.Id == userId))
            .SingleAsync(cancellationToken);

        return new ChangeUserResult.Changed(user);
    }

    private static bool IsDemotion(UserRole from, UserRole to) => Rank(to) < Rank(from);

    private static int Rank(UserRole role) => role switch
    {
        UserRole.Manager => 2,
        UserRole.Technician => 1,
        _ => 0
    };

    private IQueryable<ManagedUser> Project(IQueryable<User> users) =>
        users.Select(u => new ManagedUser(
            u.Id,
            u.Name,
            u.Email,
            u.Role,
            u.JobTitleId,
            u.JobTitle == null ? null : u.JobTitle.Name,
            u.IsActive,
            u.MustChangePassword,
            u.CreatedAt,
            db.Tickets.Count(t => t.AssignedTechnicianId == u.Id
                                  && !TicketStatusMachine.Finished.Contains(t.Status))));
}
