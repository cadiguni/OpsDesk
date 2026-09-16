using Microsoft.EntityFrameworkCore;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Application.Abstractions;

/// <summary>
/// Superfície do <c>DbContext</c> vista pela camada de aplicação.
///
/// Existe por causa do sentido das dependências: os serviços de caso de uso moram na
/// Application, o <c>OpsDeskDbContext</c> mora na Infrastructure, e é a Infrastructure que
/// referencia a Application — não o contrário.
///
/// **Isto não é o repositório genérico que a decisão 4.9 descarta.** Um repositório
/// genérico troca o modelo do EF por métodos como <c>GetAll</c> e <c>FindBy</c>, e nisso
/// esconde a query. Aqui a interface expõe os mesmos <see cref="DbSet{T}"/> do contexto
/// real: os serviços continuam escrevendo LINQ com <c>Where</c>, <c>Include</c> e
/// projeção por <c>Select</c>, e o filtro de visibilidade continua sendo aplicado no
/// <see cref="IQueryable{T}"/>. Nenhuma capacidade do EF Core é perdida, e nenhuma
/// camada de tradução aparece no meio.
/// </summary>
public interface IOpsDeskDbContext
{
    DbSet<User> Users { get; }

    DbSet<Ticket> Tickets { get; }

    DbSet<Category> Categories { get; }

    DbSet<TicketComment> TicketComments { get; }

    DbSet<TicketHistory> TicketHistory { get; }

    DbSet<SlaPolicy> SlaPolicies { get; }

    DbSet<Holiday> Holidays { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
