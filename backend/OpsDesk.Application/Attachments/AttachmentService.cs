using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Application.Attachments;

/// <summary>
/// Anexos de chamado.
///
/// O fluxo é em dois tempos, e é o que sustenta a invariante 2 para arquivos: o envio
/// cria um anexo <b>pendente</b>, visível só para quem enviou, e o vínculo — na abertura
/// do chamado ou no comentário — é o que decide a quem ele passa a ser visível. Não
/// existe momento em que um print destinado a uma nota interna esteja legível para o
/// solicitante.
///
/// O binário vai para o <see cref="IAttachmentStorage"/>; o banco guarda metadado.
/// </summary>
public class AttachmentService(IOpsDeskDbContext db, IAttachmentStorage storage, IClock clock)
{
    /// <summary>Dez megabytes. Print de tela e log cabem; vídeo e dump de banco, não.</summary>
    public const long MaxFileSizeInBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Anexos pendentes que um usuário pode ter ao mesmo tempo.
    ///
    /// O limite por arquivo não limita nada sozinho: sem teto de quantidade, mil envios de
    /// dez megabytes são dez gigabytes. O estado pendente é o ponto certo para cobrar isso,
    /// porque é o único em que o arquivo existe sem pertencer a nada — e onde um cliente
    /// com defeito, ou de má-fé, acumularia para sempre. Vincular libera a cota.
    /// </summary>
    public const int MaxPendingPerUser = 20;

    /// <summary>
    /// Anexos por chamado, somando abertura e todos os comentários.
    ///
    /// Generoso de propósito: uma conversa longa com print a cada resposta chega perto
    /// disso legitimamente. É um teto contra abuso, não uma regra de negócio.
    /// </summary>
    public const int MaxPerTicket = 50;

    /// <summary>
    /// Tipos aceitos.
    ///
    /// Lista fechada em vez de lista de bloqueio: o que não foi previsto é recusado, e não
    /// aceito por omissão. Cobre o que chega de verdade num service desk — print, foto do
    /// erro, PDF, planilha, log e o zip com tudo isso dentro.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "image/bmp",
        "application/pdf",
        "text/plain",
        "text/csv",
        "application/zip",
        "application/x-zip-compressed",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-outlook",
        "message/rfc822"
    };

    public async Task<UploadAttachmentResult> UploadAsync(
        UploadAttachmentRequest request,
        TicketViewer uploader,
        CancellationToken cancellationToken = default)
    {
        if (request.SizeInBytes <= 0)
        {
            return new UploadAttachmentResult.Empty();
        }

        if (request.SizeInBytes > MaxFileSizeInBytes)
        {
            return new UploadAttachmentResult.TooLarge(MaxFileSizeInBytes);
        }

        var contentType = Normalize(request.ContentType);

        if (!AllowedContentTypes.Contains(contentType))
        {
            return new UploadAttachmentResult.TypeNotAllowed(contentType);
        }

        var pending = await db.TicketAttachments.CountAsync(
            a => a.TicketId == null && a.UploadedById == uploader.UserId, cancellationToken);

        if (pending >= MaxPendingPerUser)
        {
            return new UploadAttachmentResult.TooManyPending(MaxPendingPerUser);
        }

        var now = clock.UtcNow;

        var attachment = new TicketAttachment
        {
            UploadedById = uploader.UserId,
            FileName = SafeFileName(request.FileName),
            ContentType = contentType,
            SizeInBytes = request.SizeInBytes,
            CreatedAt = now
        };

        attachment.StorageKey = BuildStorageKey(attachment, now);

        // Grava o arquivo antes do registro: registro sem arquivo produz um anexo que a
        // interface mostra e o download não encontra. Arquivo sem registro é só espaço em
        // disco, recolhido pela varredura de pendentes.
        await storage.SaveAsync(attachment.StorageKey, request.Content, cancellationToken);

        db.TicketAttachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);

        var item = await db.TicketAttachments
            .AsNoTracking()
            .Where(a => a.Id == attachment.Id)
            .Select(Projection)
            .SingleAsync(cancellationToken);

        return new UploadAttachmentResult.Uploaded(item);
    }

    /// <summary>
    /// Vincula anexos pendentes ao chamado recém-aberto ou a um comentário.
    ///
    /// Só entra anexo que está pendente e que foi enviado por quem está vinculando: sem
    /// isso, mandar o identificador de um anexo alheio o traria para dentro de um chamado
    /// visível a quem não deveria vê-lo.
    ///
    /// Não chama <c>SaveChangesAsync</c> — o vínculo participa da mesma transação da
    /// abertura ou do comentário. Devolve <c>false</c> quando algum identificador não
    /// serve, e aí a operação inteira é recusada em vez de gravar um chamado sem o anexo
    /// que a pessoa achou que tinha mandado.
    /// </summary>
    public async Task<bool> TryBindAsync(
        IReadOnlyCollection<Guid> attachmentIds,
        Guid ticketId,
        Guid? commentId,
        bool isInternal,
        TicketViewer uploader,
        CancellationToken cancellationToken = default)
    {
        if (attachmentIds.Count == 0)
        {
            return true;
        }

        var attachments = await db.TicketAttachments
            .Where(a => attachmentIds.Contains(a.Id)
                        && a.TicketId == null
                        && a.UploadedById == uploader.UserId)
            .ToListAsync(cancellationToken);

        if (attachments.Count != attachmentIds.Distinct().Count())
        {
            return false;
        }

        var alreadyOnTicket = await db.TicketAttachments
            .CountAsync(a => a.TicketId == ticketId, cancellationToken);

        if (alreadyOnTicket + attachments.Count > MaxPerTicket)
        {
            return false;
        }

        foreach (var attachment in attachments)
        {
            attachment.TicketId = ticketId;
            attachment.CommentId = commentId;

            // O anexo herda a visibilidade do comentário. É a única escrita deste campo.
            attachment.IsInternal = isInternal;
        }

        return true;
    }

    /// <summary>
    /// Anexos de um chamado visíveis para quem pediu, já sem os pendentes de terceiros.
    ///
    /// Chamado fora da visibilidade devolve lista vazia, indistinguível de chamado sem
    /// anexo — mesmo comportamento dos comentários.
    /// </summary>
    public Task<List<AttachmentItem>> ListAsync(
        Guid ticketId, TicketViewer viewer, CancellationToken cancellationToken = default) =>
        db.TicketAttachments
            .AsNoTracking()
            .VisibleTo(viewer)
            .Where(a => a.TicketId == ticketId)
            .OrderBy(a => a.CreatedAt)
            .Select(Projection)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Abre o conteúdo do anexo, ou <c>null</c> quando não existe, não é visível, ou o
    /// arquivo sumiu do armazenamento. Os três casos respondem igual pelo mesmo motivo de
    /// sempre: distinguir confirmaria a existência do anexo a quem não pode vê-lo.
    /// </summary>
    public async Task<AttachmentContent?> OpenAsync(
        Guid id, TicketViewer viewer, CancellationToken cancellationToken = default)
    {
        var attachment = await db.TicketAttachments
            .AsNoTracking()
            .VisibleTo(viewer)
            .Where(a => a.Id == id)
            .Select(a => new { a.StorageKey, a.ContentType, a.FileName })
            .SingleOrDefaultAsync(cancellationToken);

        if (attachment is null)
        {
            return null;
        }

        var content = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken);

        return content is null
            ? null
            : new AttachmentContent(content, attachment.ContentType, attachment.FileName);
    }

    /// <summary>
    /// Recolhe anexos pendentes velhos: enviados, nunca vinculados, e sem dono depois de
    /// <paramref name="olderThan"/>.
    ///
    /// Sem isso o arquivo que a pessoa anexou e desistiu de enviar fica para sempre, no
    /// banco e no armazenamento. Como pendente é invisível para todos menos para quem
    /// enviou, ninguém descobre o vazamento de espaço olhando a interface — só a conta do
    /// armazenamento conta a história, meses depois.
    ///
    /// O arquivo sai antes da linha. Na ordem inversa, uma falha no meio deixaria arquivo
    /// sem registro, que nenhuma varredura futura encontraria; nesta ordem, o pior caso é
    /// uma linha pendente sem arquivo, que a próxima passagem remove.
    ///
    /// Processa em lote limitado: uma varredura que apagasse cem mil linhas numa transação
    /// seguraria o banco sem necessidade. O que sobrar espera a próxima rodada.
    /// </summary>
    public async Task<int> CleanUpPendingAsync(
        TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = clock.UtcNow - olderThan;

        var orphans = await db.TicketAttachments
            .Where(a => a.TicketId == null && a.CreatedAt < cutoff)
            .OrderBy(a => a.CreatedAt)
            .Take(CleanUpBatchSize)
            .ToListAsync(cancellationToken);

        if (orphans.Count == 0)
        {
            return 0;
        }

        foreach (var orphan in orphans)
        {
            await storage.DeleteAsync(orphan.StorageKey, cancellationToken);
        }

        db.TicketAttachments.RemoveRange(orphans);
        await db.SaveChangesAsync(cancellationToken);

        return orphans.Count;
    }

    private const int CleanUpBatchSize = 500;

    private static Expression<Func<TicketAttachment, AttachmentItem>> Projection =>
        a => new AttachmentItem(
            a.Id,
            a.CommentId,
            a.FileName,
            a.ContentType,
            a.SizeInBytes,
            a.IsInternal,
            a.ContentType.StartsWith("image/"),
            a.UploadedById,
            a.UploadedBy.Name,
            a.CreatedAt);

    private static string Normalize(string contentType)
    {
        // O navegador manda "text/plain; charset=utf-8"; o tipo é o que vem antes do ";".
        var separator = contentType.IndexOf(';');

        return (separator < 0 ? contentType : contentType[..separator]).Trim();
    }

    /// <summary>
    /// Nome para exibir e devolver no download.
    ///
    /// Fica só o nome do arquivo, sem nenhum componente de caminho: navegador tem o
    /// costume de mandar o caminho inteiro em alguns cenários, e nome com barra vira
    /// dor de cabeça no cabeçalho do download.
    /// </summary>
    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();

        return string.IsNullOrEmpty(name) ? "anexo" : Truncate(name, 260);
    }

    /// <summary>
    /// Chave do armazenamento: ano, mês e o identificador do anexo.
    ///
    /// Particionar por mês evita o diretório com centenas de milhares de arquivos, que é
    /// desconfortável de listar e ruim para alguns sistemas de arquivos. A extensão vem do
    /// nome original só para conveniência de quem for olhar o disco — nada no sistema
    /// depende dela, e o que não for letra ou número é descartado.
    /// </summary>
    private static string BuildStorageKey(TicketAttachment attachment, DateTimeOffset now)
    {
        var extension = new string(
            Path.GetExtension(attachment.FileName)
                .TrimStart('.')
                .Where(char.IsLetterOrDigit)
                .Take(8)
                .ToArray());

        var suffix = extension.Length > 0 ? $".{extension.ToLowerInvariant()}" : string.Empty;

        return $"{now:yyyy}/{now:MM}/{attachment.Id:N}{suffix}";
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
