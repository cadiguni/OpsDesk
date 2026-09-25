namespace OpsDesk.Application.Abstractions;

/// <summary>Credenciais do envio, já com o secret decifrado. Vive só durante o envio.</summary>
public record EmailSenderCredentials(
    string TenantId, string ClientId, string ClientSecret, string SenderAddress, string? SenderName);

public record EmailMessage(string ToAddress, string ToName, string Subject, string HtmlBody);

/// <summary>
/// Entrega de uma mensagem. A implementação de produção fala com o Microsoft Graph; os
/// testes trocam por uma que só registra.
/// </summary>
public interface IEmailSender
{
    /// <exception cref="EmailDeliveryException">O provedor recusou ou não respondeu.</exception>
    Task SendAsync(EmailSenderCredentials credentials, EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Falha de entrega, com mensagem apresentável ao gestor. Nunca carrega o secret nem o
/// token: a mensagem vai para a tela de configuração e para o banco.
/// </summary>
public class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);
