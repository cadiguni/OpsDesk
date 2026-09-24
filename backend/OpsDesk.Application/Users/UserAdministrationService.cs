using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Authorization;
using OpsDesk.Application.Common;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Domain.Tickets;

namespace OpsDesk.Application.Users;

/// <summary>
/// Administração de usuários pelo gestor: listar, trocar perfil, desativar e reativar.
///
/// O acesso é restrito a gestor pela política da rota, e a verificação se repete aqui
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
    ILogger<UserAdministrationService> logger)
{
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
        // da equipe é chamado que nenhum técnico vê como seu. Técnico ↔ gestor não mexe
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
    /// Confere que quem age é gestor ativo, e carrega o alvo.
    ///
    /// O perfil de quem age é conferido no banco, e não só na claim. O token de acesso
    /// carrega o perfil do momento em que foi emitido, e um gestor recém-rebaixado ou
    /// desativado seguiria com ele por até <c>Jwt:AccessTokenMinutes</c>. Para ler chamado
    /// essa janela é aceitável; para promover alguém a gestor, não.
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
                        && !TicketStatusMachine.Terminal.Contains(t.Status))
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
            u.IsActive,
            u.MustChangePassword,
            u.CreatedAt,
            db.Tickets.Count(t => t.AssignedTechnicianId == u.Id
                                  && !TicketStatusMachine.Terminal.Contains(t.Status))));
}
