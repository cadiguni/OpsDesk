using System.Collections.Concurrent;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Inbound;

namespace OpsDesk.Tests.Integration;

/// <summary>Caixa de entrada em memória: o teste deposita a mensagem, a sincronização a lê.</summary>
public class FakeMailboxReader : IMailboxReader
{
    private readonly ConcurrentDictionary<string, List<FetchedMessage>> _unread = new();

    public List<string> MarkedAsRead { get; } = [];

    /// <summary>Quando preenchido, a leitura falha com esta mensagem.</summary>
    public string? FailWith { get; set; }

    public void Reset()
    {
        _unread.Clear();
        MarkedAsRead.Clear();
        FailWith = null;
    }

    public FetchedMessage Deliver(
        string mailbox,
        string from,
        string subject,
        string body,
        string? fromName = null,
        string? messageId = null,
        string? inReplyTo = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var message = new FetchedMessage(
            Guid.NewGuid().ToString("N"),
            messageId ?? $"<{Guid.NewGuid():N}@remetente.example>",
            inReplyTo,
            inReplyTo,
            fromName ?? "",
            from,
            mailbox,
            subject,
            body,
            DateTimeOffset.UtcNow,
            headers ?? new Dictionary<string, string>());

        _unread.GetOrAdd(mailbox.ToLowerInvariant(), _ => []).Add(message);

        return message;
    }

    public Task<IReadOnlyList<FetchedMessage>> FetchUnreadAsync(
        EmailSenderCredentials credentials, string mailbox, int max, CancellationToken cancellationToken = default)
    {
        if (FailWith is { } failure)
        {
            throw new EmailDeliveryException(failure);
        }

        var messages = _unread.TryGetValue(mailbox.ToLowerInvariant(), out var list) ? list.Take(max).ToList() : [];

        return Task.FromResult<IReadOnlyList<FetchedMessage>>(messages);
    }

    public Task MarkAsReadAsync(
        EmailSenderCredentials credentials, string mailbox, string providerId, CancellationToken cancellationToken = default)
    {
        MarkedAsRead.Add(providerId);

        if (_unread.TryGetValue(mailbox.ToLowerInvariant(), out var list))
        {
            list.RemoveAll(m => m.ProviderId == providerId);
        }

        return Task.CompletedTask;
    }
}
