using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Auth;
using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Api.Authentication;

/// <summary>
/// Usuário autenticado lido das claims do JWT. É a única ponte entre o HTTP e o filtro de
/// visibilidade — nenhuma camada abaixo desta conhece <c>HttpContext</c>.
/// </summary>
public class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(Claim(OpsDeskClaims.Subject), out var id) ? id : null;

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

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

    private UserRole? Role =>
        Enum.TryParse<UserRole>(Claim(OpsDeskClaims.Role), out var role) ? role : null;

    private string? Claim(string type) => accessor.HttpContext?.User.FindFirst(type)?.Value;
}
