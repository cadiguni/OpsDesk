using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Attachments;
using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;
using OpsDesk.Domain.Tickets;

namespace OpsDesk.Application.Tickets;

/// <summary>
/// Comentários do chamado (README, seção 11).
///
/// Invariante 2 do CLAUDE.md: comentário com <c>IsInternal = true</c> nunca chega ao
/// solicitante. Aqui isso é sustentado em dois pontos — na escrita, recusando que alguém
/// fora da equipe crie comentário interno; e na leitura, pelo filtro de visibilidade, que
/// corta por <c>IsInternal</c> dentro do SQL.
/// </summary>
public class TicketCommentService(
    IOpsDeskDbContext db,
    IClock clock,
    TicketWorkflowService workflow,
    AttachmentService attachments,
    ReopenPolicy reopening)
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

        var now = clock.UtcNow;

        // Resposta do solicitante a chamado resolvido ou fechado reabre o chamado (README,
        // seção 5.9). É o mesmo gesto da versão 2.0, em que responder o e-mail reabre: a
        // resposta é o motivo, e a reabertura vem junto, sem botão à parte. "Enviar e
        // fechar" é o oposto — confirmar a solução — e não reabre nada.
        var reopens = !author.IsStaff
                      && !request.CloseTicket
                      && ticket.Status is TicketStatus.Resolved or TicketStatus.Closed;

        if (ticket.Status == TicketStatus.Cancelled
            || (ticket.Status == TicketStatus.Closed && !reopens))
        {
            // A equipe não comenta em chamado fechado: reabre pela mudança de status, que
            // deixa a decisão explícita no histórico, e então comenta.
            return new AddCommentResult.TicketClosed();
        }

        if (reopens && !reopening.CanReopen(ticket.Status, ticket.ClosedAt, now))
        {
            return new AddCommentResult.ReopenWindowExpired(reopening.WindowDays);
        }

        if (reopens)
        {
            workflow.ApplySlaEffects(ticket, ticket.Status, TicketStatus.InProgress, now);
            ticket.Status = TicketStatus.InProgress;
        }

        if (request.CloseTicket)
        {
            if (!TicketStatusMachine.IsAllowedForRole(ticket.Status, TicketStatus.Closed, author.Role))
            {
                return new AddCommentResult.ClosingNotAllowed();
            }

            workflow.ApplySlaEffects(ticket, ticket.Status, TicketStatus.Closed, now);
            ticket.Status = TicketStatus.Closed;
        }

        var comment = new TicketComment
        {
            TicketId = ticket.Id,
            AuthorId = author.UserId,
            Content = request.Content.Trim(),
            IsInternal = request.IsInternal,
            CreatedAt = now
        };

        db.TicketComments.Add(comment);

        // O anexo herda a visibilidade do comentário: arquivo de nota interna é nota
        // interna. Recusar o comentário inteiro quando um identificador não serve é
        // melhor que publicá-lo sem o anexo que a pessoa achou que tinha mandado — e o
        // `return` antes do SaveChanges garante que nada foi gravado.
        if (!await attachments.TryBindAsync(
                request.AttachmentIds ?? [],
                ticket.Id,
                comment.Id,
                request.IsInternal,
                author,
                cancellationToken))
        {
            return new AddCommentResult.AttachmentsInvalid();
        }

        // Marco do SLA de resposta (README, seção 8.3): o primeiro comentário **público**
        // de técnico ou gestor. Comentário interno não conta — ele não chega ao solicitante,
        // e um SLA de resposta que se fecha com uma observação que o solicitante nunca vê
        // mediria a conversa da equipe consigo mesma.
        if (ticket.FirstRespondedAt is null && author.IsStaff && !request.IsInternal)
        {
            ticket.FirstRespondedAt = now;
        }

        // Comentário é atualização do chamado, para a ordenação "atualizados
        // recentemente". Sem isto, a resposta do solicitante — o que a equipe mais precisa
        // ver — não mexeria o chamado na fila, porque só o comentário é gravado. O valor
        // exato vem do TimestampInterceptor; aqui basta marcar o chamado como alterado.
        ticket.UpdatedAt = now;

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
