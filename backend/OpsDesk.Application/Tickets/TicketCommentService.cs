using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Application.Tickets;

/// <summary>
/// Comentários do chamado (README, seção 11).
///
/// Invariante 2 do CLAUDE.md: comentário com <c>IsInternal = true</c> nunca chega ao
/// solicitante. Aqui isso é sustentado em dois pontos — na escrita, recusando que alguém
/// fora da equipe crie comentário interno; e na leitura, pelo filtro de visibilidade, que
/// corta por <c>IsInternal</c> dentro do SQL.
/// </summary>
public class TicketCommentService(IOpsDeskDbContext db, IClock clock)
{
    public async Task<AddCommentResult> AddAsync(
        Guid ticketId,
        AddCommentRequest request,
        TicketViewer author,
        CancellationToken cancellationToken = default)
    {
        if (request.IsInternal && !author.IsStaff)
        {
            return new AddCommentResult.InternalNotAllowed();
        }

        // O chamado é carregado pelo filtro de visibilidade: chamado de terceiro simplesmente
        // não aparece, e a resposta é a mesma de chamado inexistente.
        var ticket = await db.Tickets
            .VisibleTo(author)
            .SingleOrDefaultAsync(t => t.Id == ticketId, cancellationToken);

        if (ticket is null)
        {
            return new AddCommentResult.TicketNotFound();
        }

        if (ticket.Status is Domain.Enums.TicketStatus.Closed or Domain.Enums.TicketStatus.Cancelled)
        {
            // Resolvido continua aceitando comentário de propósito: é onde o solicitante diz
            // que o problema voltou. Fechado e cancelado são finais.
            return new AddCommentResult.TicketClosed();
        }

        var now = clock.UtcNow;

        var comment = new TicketComment
        {
            TicketId = ticket.Id,
            AuthorId = author.UserId,
            Content = request.Content.Trim(),
            IsInternal = request.IsInternal,
            CreatedAt = now
        };

        db.TicketComments.Add(comment);

        // Marco do SLA de resposta (README, seção 8.3): o primeiro comentário **público**
        // de técnico ou gestor. Comentário interno não conta — ele não chega ao solicitante,
        // e um SLA de resposta que se fecha com uma observação que o solicitante nunca vê
        // mediria a conversa da equipe consigo mesma.
        if (ticket.FirstRespondedAt is null && author.IsStaff && !request.IsInternal)
        {
            ticket.FirstRespondedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var item = await db.TicketComments
            .AsNoTracking()
            .Where(c => c.Id == comment.Id)
            .Select(Projection)
            .SingleAsync(cancellationToken);

        return new AddCommentResult.Added(item);
    }

    /// <summary>
    /// Comentários visíveis para quem pediu, em ordem cronológica.
    ///
    /// Devolve lista vazia quando o chamado não é visível — indistinguível de um chamado
    /// sem comentários, que é o comportamento desejado.
    /// </summary>
    public Task<List<TicketCommentItem>> ListAsync(
        Guid ticketId, TicketViewer viewer, CancellationToken cancellationToken = default) =>
        db.TicketComments
            .AsNoTracking()
            .OfTicketVisibleTo(ticketId, viewer)
            .OrderBy(c => c.CreatedAt)
            .Select(Projection)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Projeção compartilhada entre a escrita e a leitura.
    ///
    /// Vive em um lugar só para que um campo novo não entre em uma das duas e falte na
    /// outra — e, mais importante, para que <c>IsInternal</c> não seja esquecido na
    /// resposta, já que a interface depende dele para marcar o comentário visualmente.
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<TicketComment, TicketCommentItem>> Projection =>
        c => new TicketCommentItem(
            c.Id,
            c.AuthorId,
            c.Author.Name,
            c.Author.Role,
            c.Content,
            c.IsInternal,
            c.CreatedAt);
}
