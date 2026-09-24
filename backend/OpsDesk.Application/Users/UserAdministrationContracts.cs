using OpsDesk.Application.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Users;

/// <summary>Filtro da listagem de administração. Tudo opcional; nulo quer dizer "qualquer".</summary>
public record ManagedUserFilter(string? Search, UserRole? Role, bool? IsActive)
{
    public PageRequest Page { get; init; } = new();
}

/// <summary>
/// Usuário visto pelo gestor na tela de administração.
///
/// <see cref="OpenAssignedTickets"/> está aqui para a interface avisar *antes* do clique
/// que a desativação vai ser recusada, em vez de a pessoa descobrir pelo erro.
/// </summary>
public record ManagedUser(
    Guid Id,
    string Name,
    string Email,
    UserRole Role,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset CreatedAt,
    int OpenAssignedTickets);

public record ChangeRoleRequest(UserRole Role);

public record ChangeActivationRequest(bool IsActive);

/// <summary>Chamado em aberto que impede desativar ou rebaixar o responsável.</summary>
public record BlockingTicket(Guid Id, string Code, string Title, TicketStatus Status);

public abstract record ChangeUserResult
{
    public sealed record Changed(ManagedUser User) : ChangeUserResult;

    public sealed record UserNotFound : ChangeUserResult;

    /// <summary>Quem pediu não é, ou deixou de ser, gestor ativo.</summary>
    public sealed record NotAllowed : ChangeUserResult;

    /// <summary>
    /// O gestor tentou rebaixar ou desativar a si mesmo.
    ///
    /// A regra existe para que o sistema nunca fique sem gestor: quem age é sempre um
    /// gestor ativo, e se ele não pode se tirar de lá, sobra pelo menos ele. Sem isto, o
    /// único gestor se rebaixando deixaria a instalação sem volta pela interface — e o
    /// bootstrap não ajuda, porque só age quando não existe gestor *nenhum*, ativo ou não.
    /// </summary>
    public sealed record SelfChangeNotAllowed : ChangeUserResult
    {
        public string Message =>
            "Você não pode alterar o próprio perfil nem desativar a própria conta. Peça a outro gestor.";
    }

    /// <summary>
    /// A pessoa é responsável por chamados em aberto. Desativar, ou rebaixar a
    /// solicitante, deixaria esses chamados atribuídos a quem não pode mais atendê-los —
    /// e em silêncio, porque nenhuma fila mostraria o problema. O gestor reatribui antes.
    /// </summary>
    public sealed record HasOpenAssignedTickets(IReadOnlyList<BlockingTicket> Tickets) : ChangeUserResult
    {
        public string Message =>
            $"A pessoa é responsável por {Tickets.Count} chamado(s) em aberto. Reatribua antes de continuar.";
    }
}
