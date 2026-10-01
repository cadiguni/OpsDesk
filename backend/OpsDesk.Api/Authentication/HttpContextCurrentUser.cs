using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Api.Authentication;

/// <summary>
/// Usuário autenticado lido das claims do JWT. É a única ponte entre o HTTP e o filtro de
/// visibilidade — nenhuma camada abaixo desta conhece <c>HttpContext</c>.
///
/// No processamento da caixa de entrada, quem age é a pessoa definida por
/// <see cref="IActorScope.ActAs"/>: a remetente do e-mail. O ator vale só para esta
/// instância, isto é, para o escopo de injeção em que foi definido — e o processamento
/// sempre roda num escopo próprio, criado para a mensagem. O escopo da requisição HTTP que
/// eventualmente o disparou (o "não é spam" de um técnico) continua com a identidade do
/// token.
/// </summary>
public class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser, IActorScope
{
    private (Guid UserId, UserRole Role)? _actor;

    public Guid? UserId =>
        _actor is { } actor
            ? actor.UserId
            : Guid.TryParse(Claim(OpsDeskClaims.Subject), out var id) ? id : null;

    public bool IsAuthenticated =>
        _actor is not null || accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public TicketViewer Viewer
    {
        get
        {
            if (UserId is not { } id || Role is not { } role)
            {
                // Chegar aqui em endpoint anônimo é erro de programação, não entrada
                // inválida do cliente. Falhar alto é melhor que devolver uma identidade
                // vazia que o filtro de visibilidade interpretaria como algum usuário.
                throw new InvalidOperationException(
                    "Não há usuário autenticado na requisição atual.");
            }

            return new TicketViewer(id, role);
        }
    }

    public void ActAs(Guid userId, UserRole role) => _actor = (userId, role);

    private UserRole? Role =>
        _actor is { } actor
            ? actor.Role
            : Enum.TryParse<UserRole>(Claim(OpsDeskClaims.Role), out var role) ? role : null;

    private string? Claim(string type) => accessor.HttpContext?.User.FindFirst(type)?.Value;
}
