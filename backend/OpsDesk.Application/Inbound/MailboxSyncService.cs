using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Notifications;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Inbound;

/// <summary>
/// Coleta das caixas monitoradas e processamento do que chegou (docs/integracao-email.md,
/// seções 4.1 e 4.8). Chamado periodicamente em segundo plano, e diretamente pelos testes.
///
/// Cada mensagem é processada num escopo de injeção próprio: o processamento age em nome
/// do remetente, e uma falha no meio não pode deixar mudança pela metade no contexto de
/// quem vem depois. A falha é gravada noutro escopo, limpo, e a mensagem volta à fila com
/// espera crescente — até três tentativas; depois, só pelo reprocessamento manual.
/// </summary>
public class MailboxSyncService(
    IOpsDeskDbContext db,
    IMailboxReader reader,
    EmailSettingsService emailSettings,
    IServiceScopeFactory scopes,
    IClock clock,
    ILogger<MailboxSyncService> logger)
{
    public const int MaxAttempts = 3;

    private static readonly int[] RetryDelaysInMinutes = [1, 5, 15];

    /// <summary>Cabeçalhos guardados com a mensagem: os que alguma regra consulta.</summary>
    private static readonly string[] KeptHeaders =
        ["auto-submitted", "x-auto-response-suppress", "precedence", "content-type", "authentication-results"];

    /// <returns>Quantas mensagens novas foram lidas.</returns>
    public async Task<int> SyncAsync(int maxPerAccount = 25, CancellationToken cancellationToken = default)
    {
        var settings = await db.EmailSettings.SingleOrDefaultAsync(cancellationToken);

        // As credenciais do aplicativo, e não as do envio: a leitura não exige o envio ligado.
        if (emailSettings.LoadApplicationCredentials(settings) is not { } credentials)
        {
            return 0;
        }

        var accounts = await db.EmailAccounts.Where(a => a.IsActive).ToListAsync(cancellationToken);
        var fetched = 0;

        foreach (var account in accounts)
        {
            try
            {
                var messages = await reader.FetchUnreadAsync(credentials, account.Address, maxPerAccount, cancellationToken);

                foreach (var message in messages)
                {
                    var inbound = ToInbound(account, message);
                    db.InboundEmails.Add(inbound);
                    await db.SaveChangesAsync(cancellationToken);

                    // Só depois de gravada: cair entre as duas linhas faz a mensagem ser lida de
                    // novo e virar duplicata — nunca ser perdida.
                    await reader.MarkAsReadAsync(credentials, account.Address, message.ProviderId, cancellationToken);

                    await ProcessOneAsync(inbound.Id, cancellationToken);
                    fetched++;
                }

                account.LastSyncAt = clock.UtcNow;
                account.LastError = null;
            }
            catch (Exception ex) when (ex is EmailDeliveryException or HttpRequestException)
            {
                account.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                logger.LogWarning(ex, "Falha ao ler a caixa {Mailbox}.", account.Address);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await RetryFailedAsync(cancellationToken);

        return fetched;
    }

    /// <summary>Reprocessamento manual, da tela de e-mails recebidos.</summary>
    public async Task ReprocessAsync(Guid inboundId, CancellationToken cancellationToken = default)
    {
        await ProcessOneAsync(inboundId, cancellationToken, manual: true);
    }

    private async Task RetryFailedAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var due = await db.InboundEmails
            .Where(e => e.Status == InboundEmailStatus.Failed && e.Attempts < MaxAttempts && e.NextAttemptAt <= now)
            .OrderBy(e => e.ReceivedAt)
            .Select(e => e.Id)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var id in due)
        {
            await ProcessOneAsync(id, cancellationToken);
        }
    }

    private async Task ProcessOneAsync(Guid inboundId, CancellationToken cancellationToken, bool manual = false)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<InboundEmailProcessor>().ProcessAsync(inboundId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao processar o e-mail recebido {InboundId}.", inboundId);

            await using var scope = scopes.CreateAsyncScope();
            var clean = scope.ServiceProvider.GetRequiredService<IOpsDeskDbContext>();
            var email = await clean.InboundEmails.SingleAsync(e => e.Id == inboundId, cancellationToken);

            email.Attempts = manual ? Math.Max(email.Attempts, 1) : email.Attempts + 1;
            email.Status = InboundEmailStatus.Failed;
            email.ProcessingError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            email.NextAttemptAt = email.Attempts < MaxAttempts
                ? clock.UtcNow.AddMinutes(RetryDelaysInMinutes[Math.Min(email.Attempts, RetryDelaysInMinutes.Length) - 1])
                : null;

            await clean.SaveChangesAsync(cancellationToken);
        }
    }

    private InboundEmail ToInbound(EmailAccount account, FetchedMessage message) => new()
    {
        EmailAccountId = account.Id,
        MessageId = Limit(message.MessageId?.Trim(), 998),
        ProviderId = message.ProviderId.Length <= 512 ? message.ProviderId : null,
        InReplyTo = Limit(message.InReplyTo, 998),
        References = Limit(message.References, 8000),
        FromName = Limit(message.FromName, 200) ?? "",
        FromAddress = Limit(message.FromAddress.Trim().ToLowerInvariant(), 320)!,
        ToAddress = Limit(message.ToAddress, 320),
        Subject = Limit(message.Subject, 998) ?? "",
        BodyText = Limit(message.BodyText, 100_000) ?? "",
        HeadersJson = JsonSerializer.Serialize(
            message.Headers.Where(h => KeptHeaders.Contains(h.Key)).ToDictionary(h => h.Key, h => h.Value)),
        ReceivedAt = message.ReceivedAt.ToUniversalTime(),
        Status = InboundEmailStatus.Pending,
        CreatedAt = clock.UtcNow
    };

    private static string? Limit(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
