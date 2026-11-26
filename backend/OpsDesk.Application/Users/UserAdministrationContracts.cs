using OpsDesk.Application.Common;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Users;

/// <summary>Filtro da listagem de administração. Tudo opcional; nulo quer dizer "qualquer".</summary>
public record ManagedUserFilter(string? Search, UserRole? Role, bool? IsActive)
{
    public PageRequest Page { get; init; } = new();
}

/// <summary>
/// Usuário visto pelo administrador na tela de administração.
///
/// <see cref="OpenAssignedTickets"/> está aqui para a interface avisar *antes* do clique
/// que a desativação vai ser recusada, em vez de a pessoa descobrir pelo erro.
/// </summary>
public record ManagedUser(
    Guid Id,
    string Name,
    string Email,
    UserRole Role,
    Guid? JobTitleId,
    string? JobTitleName,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset CreatedAt,
    int OpenAssignedTickets);

public record ChangeRoleRequest(UserRole Role);

public record ChangeActivationRequest(bool IsActive);

/// <summary>
/// Cadastro pelo administrador. Não passa pela política de autocadastro: é o administrador
/// quem decide. O cargo é opcional e não muda o que a pessoa pode fazer.
/// </summary>
public record CreateUserRequest(string Name, string Email, UserRole Role, Guid? JobTitleId = null);

/// <summary>Troca o cargo. Nulo tira o cargo da pessoa.</summary>
public record ChangeJobTitleRequest(Guid? JobTitleId);

/// <summary>
/// Como a pessoa recebe o acesso. Com e-mail disponível, um convite com link para definir
/// a própria senha — e o administrador nunca vê senha nenhuma. Sem e-mail, uma senha provisória,
/// mostrada uma vez ao administrador e trocada no primeiro login.
/// </summary>
public record AccessGranted(ManagedUser User, bool InvitationSent, string? TemporaryPassword);

/// <summary>Evento da trilha de auditoria de usuário, para a tela.</summary>
public record UserAuditItem(
    UserAuditAction Action, string? PreviousValue, string? NewValue, string? ChangedByName, DateTimeOffset CreatedAt);

/// <summary>Chamado em aberto que impede desativar ou rebaixar o responsável.</summary>
public record BlockingTicket(Guid Id, string Code, string Title, TicketStatus Status);

public abstract record ChangeUserResult
{
    public sealed record Changed(ManagedUser User) : ChangeUserResult;

    public sealed record AccessIssued(AccessGranted Access) : ChangeUserResult;

    public sealed record EmailAlreadyUsed : ChangeUserResult
    {
        public string Message => "Já existe uma conta com este e-mail.";
    }

    public sealed record UserNotFound : ChangeUserResult;

    /// <summary>
    /// O cargo escolhido não existe ou está desativado. Quem já tem um cargo desativado
    /// continua com ele; o que não se faz é atribuí-lo de novo.
    /// </summary>
    public sealed record JobTitleUnavailable : ChangeUserResult
    {
        public string Message => "Cargo inexistente ou desativado.";
    }

    /// <summary>Quem pediu não é, ou deixou de ser, administrador ativo.</summary>
    public sealed record NotAllowed : ChangeUserResult;

    /// <summary>
    /// O administrador tentou rebaixar ou desativar a si mesmo.
    ///
    /// A regra existe para que o sistema nunca fique sem administrador: quem age é sempre
    /// um administrador ativo, e se ele não pode se tirar de lá, sobra pelo menos ele. Sem
    /// isto, o único administrador se rebaixando deixaria a instalação sem volta pela interface — e o
    /// bootstrap não ajuda, porque só age quando não existe administrador *nenhum*, ativo ou não.
    /// </summary>
    public sealed record SelfChangeNotAllowed : ChangeUserResult
    {
        public string Message =>
            "Você não pode alterar o próprio perfil nem desativar a própria conta. Peça a outro administrador.";
    }

    /// <summary>
    /// A pessoa é responsável por chamados em aberto. Desativar, ou rebaixar a
    /// solicitante, deixaria esses chamados atribuídos a quem não pode mais atendê-los —
    /// e em silêncio, porque nenhuma fila mostraria o problema. O administrador reatribui antes.
    /// </summary>
    public sealed record HasOpenAssignedTickets(IReadOnlyList<BlockingTicket> Tickets) : ChangeUserResult
    {
        public string Message =>
            $"A pessoa é responsável por {Tickets.Count} chamado(s) em aberto. Reatribua antes de continuar.";
    }
}
