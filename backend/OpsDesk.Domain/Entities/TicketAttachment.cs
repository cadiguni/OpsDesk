using OpsDesk.Domain.Common;

namespace OpsDesk.Domain.Entities;

/// <summary>
/// Arquivo anexado a um chamado — print da tela, log, planilha, o que a pessoa mandaria
/// por e-mail.
///
/// O conteúdo do arquivo **não** fica no banco: fica no armazenamento configurado, e aqui
/// ficam os metadados e a chave para encontrá-lo. O banco guarda o que precisa ser
/// consultado e filtrado; o binário não é nenhum dos dois.
///
/// Três estados, e a ordem entre eles importa para não vazar dado:
///
/// 1. <b>pendente</b> — <see cref="TicketId"/> nulo. Foi enviado, mas ainda não pertence
///    a chamado nenhum: é o arquivo que a pessoa soltou no formulário antes de clicar em
///    "abrir chamado" ou em "enviar". Só quem enviou enxerga.
/// 2. <b>do chamado</b> — <see cref="TicketId"/> preenchido e <see cref="CommentId"/>
///    nulo. É o anexo da abertura, e acompanha a visibilidade do chamado.
/// 3. <b>do comentário</b> — os dois preenchidos. Herda o
///    <see cref="IsInternal"/> do comentário no momento em que é vinculado.
///
/// O anexo carrega a própria cópia de <see cref="IsInternal"/> em vez de consultar o
/// comentário na leitura. É redundância deliberada: o filtro de visibilidade fica sem
/// junção e sem caminho alternativo, e o vínculo é o único lugar que escreve esse campo.
/// </summary>
public class TicketAttachment : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Nulo enquanto o anexo está pendente de vínculo.</summary>
    public Guid? TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    /// <summary>Preenchido quando o anexo pertence a um comentário, e não à abertura.</summary>
    public Guid? CommentId { get; set; }

    public TicketComment? Comment { get; set; }

    public Guid UploadedById { get; set; }

    public User UploadedBy { get; set; } = null!;

    /// <summary>Nome original, para devolver no download. Nunca é usado como caminho.</summary>
    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long SizeInBytes { get; set; }

    /// <summary>
    /// Chave no armazenamento, gerada pelo sistema a partir do identificador. Não vem do
    /// nome que o usuário mandou: nome de arquivo é entrada não confiável e viraria
    /// travessia de diretório no primeiro <c>../</c>.
    /// </summary>
    public string StorageKey { get; set; } = null!;

    /// <summary>Herda do comentário ao qual o anexo foi vinculado.</summary>
    public bool IsInternal { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
