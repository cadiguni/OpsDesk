using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Attachments;
using OpsDesk.Application.Notifications;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Inbound;

/// <summary>Resultado de um lote da recuperação.</summary>
/// <param name="Examined">Mensagens olhadas neste lote.</param>
/// <param name="Recovered">Mensagens que ganharam pelo menos um anexo.</param>
/// <param name="Attachments">Anexos trazidos.</param>
/// <param name="NotFound">Mensagens que não estão mais na caixa de correio, ou cujo destino não foi achado.</param>
/// <param name="Remaining">Mensagens ainda por olhar depois deste lote.</param>
/// <param name="Cursor">De onde o próximo lote continua; nulo quando acabou.</param>
/// <param name="Error">Falha do Microsoft Graph que interrompeu o lote.</param>
public record AttachmentRecoveryBatch(
    int Examined,
    int Recovered,
    int Attachments,
    int NotFound,
    int Remaining,
    Guid? Cursor,
    string? Error);

/// <summary>
/// Traz os anexos das mensagens lidas antes de o OpsDesk guardar o identificador do
/// provedor (docs/integracao-email.md, 4.7.1). Para cada uma, procura a mensagem na caixa
/// pelo Message-ID, baixa os anexos e os põe onde a mensagem foi parar: na abertura do
/// chamado ou no comentário da resposta.
///
/// Trabalha em lotes, para caber numa requisição: quem chama repete com o cursor até acabar.
///
/// Mexe no mínimo. O texto troca as marcas <c>[cid:…]</c> por <c>ExecuteUpdate</c>, que não
/// passa pelos interceptores: corrigir a exibição de uma imagem não é evento do chamado, não
/// entra no histórico, não avisa ninguém e não muda o "atualizado em". Os anexos entram já
/// vinculados, em nome de quem mandou a mensagem, com a visibilidade do que os recebe.
///
/// Repetir é seguro: a mensagem recuperada ganha o <c>ProviderId</c> e sai da fila, e o
/// texto é trocado antes dos anexos entrarem — cair entre os dois deixa a mensagem na fila,
/// e a nova tentativa não acha mais marca para trocar.
/// </summary>
public class InboundAttachmentRecovery(
    IOpsDeskDbContext db,
    AttachmentService attachments,
    IMailboxReader reader,
    EmailSettingsService emailSettings,
    ILogger<InboundAttachmentRecovery> logger)
{
    public const int BatchSize = 20;

    /// <summary>Distância máxima entre o processamento da mensagem e o comentário que ela gerou.</summary>
    private static readonly TimeSpan CommentWindow = TimeSpan.FromMinutes(10);

    private IQueryable<InboundEmail> Pending() =>
        db.InboundEmails.Where(e =>
            e.Status == InboundEmailStatus.Processed
            && e.TicketId != null
            && e.ProviderId == null
            && e.MessageId != null);

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
        Pending().CountAsync(cancellationToken);

    public async Task<AttachmentRecoveryBatch> RunBatchAsync(Guid? after, CancellationToken cancellationToken = default)
    {
        var settings = await db.EmailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        if (emailSettings.LoadApplicationCredentials(settings) is not { } credentials)
        {
            return new AttachmentRecoveryBatch(0, 0, 0, 0, await CountPendingAsync(cancellationToken), after,
                "O aplicativo do Entra ID não está configurado, ou o client secret não pode ser lido.");
        }

        // Pela ordem de leitura (o Id é versão 7, crescente no tempo): a abertura vem antes das
        // respostas, e é ela que fica com a imagem da assinatura que as respostas repetem.
        var query = Pending();

        if (after is { } cursor)
        {
            query = query.Where(e => e.Id.CompareTo(cursor) > 0);
        }

        var batch = await query
            .Include(e => e.EmailAccount)
            .OrderBy(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        int recovered = 0, files = 0, notFound = 0, examined = 0;
        Guid? last = after;
        string? error = null;

        foreach (var email in batch)
        {
            try
            {
                var count = await RecoverAsync(email, credentials, cancellationToken);

                if (count is null)
                {
                    notFound++;
                }
                else if (count > 0)
                {
                    recovered++;
                    files += count.Value;
                }
            }
            catch (EmailDeliveryException exception)
            {
                // Credencial recusada ou permissão faltando valem para o lote inteiro: parar
                // aqui, com o cursor antes desta mensagem, para a nova tentativa começar dela.
                logger.LogWarning(exception, "Recuperação de anexos interrompida no e-mail {Id}.", email.Id);
                error = exception.Message;
                break;
            }

            examined++;
            last = email.Id;
        }

        var remaining = last is { } next
            ? await Pending().CountAsync(e => e.Id.CompareTo(next) > 0, cancellationToken)
            : await CountPendingAsync(cancellationToken);

        return new AttachmentRecoveryBatch(
            examined, recovered, files, notFound, remaining, remaining > 0 || error is not null ? last : null, error);
    }

    /// <summary>Anexos trazidos, ou nulo quando a mensagem ou o destino dela não foi achado.</summary>
    private async Task<int?> RecoverAsync(InboundEmail email, EmailSenderCredentials credentials, CancellationToken cancellationToken)
    {
        var mailbox = email.EmailAccount.Address;

        if (await reader.FindByMessageIdAsync(credentials, mailbox, email.MessageId!, cancellationToken) is not { } providerId
            || providerId.Length > 512)
        {
            return null;
        }

        var ticket = await db.Tickets.AsNoTracking()
            .Where(t => t.Id == email.TicketId)
            .Select(t => new { t.Id, t.RequesterId, t.Source, t.ExternalId, t.Description, t.CreatedAt })
            .SingleAsync(cancellationToken);

        // Abertura: o chamado nasceu desta mensagem. Senão, foi resposta — e o comentário é o
        // do remetente gravado junto com o processamento dela.
        var opening = ticket.Source == TicketSource.Email && ticket.ExternalId == email.MessageId;
        TicketComment? comment = null;

        if (!opening)
        {
            comment = await ReplyCommentAsync(email, cancellationToken);

            if (comment is null)
            {
                return null;
            }
        }

        var uploaderId = comment?.AuthorId ?? ticket.RequesterId;
        var fetched = await reader.FetchAttachmentsAsync(
            credentials, mailbox, providerId, AttachmentService.MaxFileSizeInBytes, cancellationToken);

        var imported = new List<InboundEmailProcessor.ImportedAttachment>();

        foreach (var file in fetched)
        {
            if (await attachments.StagePendingAsync(file.FileName, file.ContentType, file.Content, uploaderId, cancellationToken)
                is { } attachment)
            {
                imported.Add(new(attachment, file.IsInline, InboundEmailProcessor.NormalizeContentId(file.ContentId)));
            }
        }

        var keep = (await InboundEmailProcessor.AttachableAsync(db, ticket.Id, imported, cancellationToken)).ToHashSet();

        // O texto primeiro (ver o resumo da classe).
        if (comment is null)
        {
            var description = InboundEmailProcessor.ReplaceInlineImages(ticket.Description, imported);

            if (description != ticket.Description)
            {
                await db.Tickets.Where(t => t.Id == ticket.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(t => t.Description, description), cancellationToken);
            }
        }
        else
        {
            var content = InboundEmailProcessor.ReplaceInlineImages(comment.Content, imported);

            if (content != comment.Content)
            {
                await db.TicketComments.Where(c => c.Id == comment.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(c => c.Content, content), cancellationToken);
            }
        }

        foreach (var item in imported)
        {
            if (keep.Contains(item.Attachment.Id))
            {
                item.Attachment.TicketId = ticket.Id;
                item.Attachment.CommentId = comment?.Id;
                item.Attachment.IsInternal = comment?.IsInternal ?? false;
                item.Attachment.CreatedAt = comment?.CreatedAt ?? ticket.CreatedAt;
            }
            else
            {
                // Repetido ou além do teto: não chega a existir. O arquivo já gravado fica
                // órfão no armazenamento, como o de um envio pendente abandonado.
                db.TicketAttachments.Remove(item.Attachment);
            }
        }

        email.ProviderId = providerId;
        await db.SaveChangesAsync(cancellationToken);

        return keep.Count;
    }

    private async Task<TicketComment?> ReplyCommentAsync(InboundEmail email, CancellationToken cancellationToken)
    {
        if (email.ProcessedAt is not { } processedAt)
        {
            return null;
        }

        var address = email.FromAddress.Trim().ToLowerInvariant();
        var authorId = await db.Users.Where(u => u.Email == address).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);

        if (authorId is null)
        {
            return null;
        }

        var from = processedAt - CommentWindow;
        var to = processedAt + CommentWindow;

        var candidates = await db.TicketComments.AsNoTracking()
            .Where(c => c.TicketId == email.TicketId && c.AuthorId == authorId && c.CreatedAt >= from && c.CreatedAt <= to)
            .ToListAsync(cancellationToken);

        return candidates.MinBy(c => (c.CreatedAt - processedAt).Duration());
    }
}
