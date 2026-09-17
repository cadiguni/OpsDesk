namespace OpsDesk.Application.Abstractions;

/// <summary>
/// Onde o conteúdo dos anexos fica guardado.
///
/// A aplicação só conhece esta interface. A versão 1 grava em disco, com volume no
/// compose; trocar por S3 ou MinIO é escrever outra implementação, sem tocar em serviço
/// nem em domínio. A chave é opaca e gerada pelo sistema — nunca o nome do arquivo que o
/// usuário mandou.
/// </summary>
public interface IAttachmentStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre o conteúdo para leitura, ou devolve <c>null</c> quando a chave não existe mais
    /// no armazenamento — o registro no banco pode sobreviver a um arquivo perdido, e a
    /// API precisa distinguir isso de "não é seu".
    /// </summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
