using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Users;

/// <summary>Usuário ativo, para o seletor de solicitante.</summary>
public record UserOption(Guid Id, string Name, string Email, UserRole Role);

/// <summary>
/// Consulta de usuários para os seletores da equipe.
///
/// Existe separado do <c>TicketService</c> porque não é leitura de chamado e não passa
/// pelo filtro de visibilidade de chamado — é catálogo de gente. O acesso é restrito à
/// equipe pela política da rota: solicitante não tem por que enumerar os colegas.
/// </summary>
public class UserDirectoryService(IOpsDeskDbContext db)
{
    /// <summary>
    /// Teto de linhas devolvidas. O seletor de solicitante carrega a lista inteira, e uma
    /// resposta sem limite viraria um problema silencioso à medida que a base crescesse.
    /// Acima disso, quem procura usa a busca por nome ou e-mail.
    /// </summary>
    public const int MaxResults = 100;

    public Task<List<UserOption>> SearchAsync(
        string? search, CancellationToken cancellationToken = default)
    {
        var query = db.Users.AsNoTracking().Where(u => u.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            // `ToLower()` nos dois lados em vez do `ILIKE` do PostgreSQL, pelo mesmo motivo
            // da busca de chamados: `EF.Functions.ILike` vem do provider Npgsql e amarraria
            // a camada de aplicação ao banco.
            query = query.Where(u =>
                u.Name.ToLower().Contains(term) || u.Email.ToLower().Contains(term));
        }

        return query
            .OrderBy(u => u.Name)
            .Take(MaxResults)
            .Select(u => new UserOption(u.Id, u.Name, u.Email, u.Role))
            .ToListAsync(cancellationToken);
    }
}
