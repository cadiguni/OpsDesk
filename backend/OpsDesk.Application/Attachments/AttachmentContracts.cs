namespace OpsDesk.Application.Attachments;

/// <summary>Anexo como a interface o enxerga. O conteúdo vem pelo download, à parte.</summary>
public record AttachmentItem(
    Guid Id,

    /// <summary>Nulo no anexo da abertura; preenchido no anexo de um comentário.</summary>
    Guid? CommentId,
    string FileName,
    string ContentType,
    long SizeInBytes,
    bool IsInternal,
    bool IsImage,
    Guid UploadedById,
    string UploadedByName,
    DateTimeOffset CreatedAt);

/// <summary>Arquivo recebido, já desembrulhado do multipart.</summary>
public record UploadAttachmentRequest(
    string FileName, string ContentType, long SizeInBytes, Stream Content);

public abstract record UploadAttachmentResult
{
    public sealed record Uploaded(AttachmentItem Attachment) : UploadAttachmentResult;

    public sealed record Empty : UploadAttachmentResult
    {
        public string Message => "O arquivo está vazio.";
    }

    public sealed record TooLarge(long Limit) : UploadAttachmentResult
    {
        public string Message => $"O arquivo passa do limite de {Limit / (1024 * 1024)} MB.";
    }

    public sealed record TypeNotAllowed(string ContentType) : UploadAttachmentResult
    {
        public string Message =>
            $"Arquivos do tipo {ContentType} não são aceitos como anexo.";
    }
}

/// <summary>
/// Conteúdo pronto para ser transmitido, com o que o cabeçalho da resposta precisa.
/// </summary>
public record AttachmentContent(Stream Content, string ContentType, string FileName);
