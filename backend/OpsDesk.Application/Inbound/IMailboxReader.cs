using OpsDesk.Application.Abstractions;

namespace OpsDesk.Application.Inbound;

/// <summary>Mensagem como o provedor a entrega, antes de qualquer decisão.</summary>
/// <param name="ProviderId">Identificador do provedor, para marcar como lida.</param>
/// <param name="Headers">Cabeçalhos da mensagem, pelo nome em minúsculas.</param>
public record FetchedMessage(
    string ProviderId,
    string? MessageId,
    string? InReplyTo,
    string? References,
    string FromName,
    string FromAddress,
    string? ToAddress,
    string Subject,
    string BodyText,
    DateTimeOffset ReceivedAt,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Leitura da caixa monitorada (docs/integracao-email.md, seção 4.1). A implementação de
/// produção fala com o Microsoft Graph; os testes trocam por uma em memória.
/// </summary>
public interface IMailboxReader
{
    /// <summary>Mensagens não lidas, das mais antigas para as mais novas.</summary>
    Task<IReadOnlyList<FetchedMessage>> FetchUnreadAsync(
        EmailSenderCredentials credentials, string mailbox, int max, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca como lida. Só é chamado depois de a mensagem estar gravada: cair entre as duas
    /// coisas faz a mensagem ser lida de novo — e virar duplicata, não perda.
    /// </summary>
    Task MarkAsReadAsync(
        EmailSenderCredentials credentials, string mailbox, string providerId, CancellationToken cancellationToken = default);
}
