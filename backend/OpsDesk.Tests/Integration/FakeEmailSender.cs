using System.Collections.Concurrent;
using OpsDesk.Application.Abstractions;

namespace OpsDesk.Tests.Integration;

/// <summary>Envio de e-mail que só registra, e que falha quando o teste pede.</summary>
public class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<(EmailSenderCredentials Credentials, EmailMessage Message)> Sent { get; } = new();

    /// <summary>Quando preenchido, todo envio falha com esta mensagem.</summary>
    public string? FailWith { get; set; }

    public void Reset()
    {
        Sent.Clear();
        FailWith = null;
    }

    public Task SendAsync(
        EmailSenderCredentials credentials, EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (FailWith is { } failure)
        {
            throw new EmailDeliveryException(failure);
        }

        Sent.Enqueue((credentials, message));

        return Task.CompletedTask;
    }
}
