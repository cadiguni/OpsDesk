using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Notifications;

/// <summary>
/// Configuração vista pelo gestor. Não há campo com o secret, de propósito: a tela sabe
/// se ele existe, se ainda dá para decifrá-lo e quando foi trocado — nunca o valor.
/// </summary>
public record EmailSettingsView(
    bool IsEnabled,
    string? SenderAddress,
    string? SenderName,
    string? TenantId,
    string? ClientId,
    bool HasClientSecret,
    bool ClientSecretReadable,
    DateTimeOffset? ClientSecretUpdatedAt,
    DateOnly? ClientSecretExpiresOn,
    string? PortalUrl,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSucceededAt,
    string? LastError,
    int PendingCount,
    int FailedCount);

/// <summary>
/// Gravação da configuração. <see cref="ClientSecret"/> nulo ou vazio quer dizer "mantenha
/// o atual" — é assim que a tela salva o resto sem nunca ter conhecido o secret.
/// </summary>
public record UpdateEmailSettingsRequest(
    bool IsEnabled,
    string? SenderAddress,
    string? SenderName,
    string? TenantId,
    string? ClientId,
    string? ClientSecret,
    DateOnly? ClientSecretExpiresOn,
    string? PortalUrl);

public record SendTestEmailRequest(string ToAddress);

public abstract record UpdateEmailSettingsResult
{
    public sealed record Saved(EmailSettingsView Settings) : UpdateEmailSettingsResult;

    /// <summary>Ligar o envio sem o que ele precisa para funcionar.</summary>
    public sealed record Incomplete(IReadOnlyList<string> Missing) : UpdateEmailSettingsResult
    {
        public string Message => $"Para ligar o envio, preencha: {string.Join(", ", Missing)}.";
    }
}

public abstract record SendTestEmailResult
{
    public sealed record Sent : SendTestEmailResult;

    public sealed record NotConfigured(string Message) : SendTestEmailResult;

    public sealed record Failed(string Message) : SendTestEmailResult;
}

/// <summary>
/// Configuração do envio de e-mail (README, seção 18, versão 1.2). Só gestor, pela
/// política da rota.
/// </summary>
public class EmailSettingsService(
    IOpsDeskDbContext db,
    ISecretProtector protector,
    IEmailSender sender,
    IClock clock,
    ILogger<EmailSettingsService> logger)
{
    public const string AuditArea = "Email";

    public async Task<EmailSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.EmailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken)
                       ?? new EmailSettings();

        return await ViewAsync(settings, cancellationToken);
    }

    public async Task<UpdateEmailSettingsResult> UpdateAsync(
        UpdateEmailSettingsRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        var settings = await db.EmailSettings.SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new EmailSettings();
            db.EmailSettings.Add(settings);
        }

        var newSecret = string.IsNullOrWhiteSpace(request.ClientSecret) ? null : request.ClientSecret.Trim();

        if (request.IsEnabled)
        {
            var hasUsableSecret = newSecret is not null
                                  || (settings.ProtectedClientSecret is { } stored
                                      && protector.TryUnprotect(stored) is not null);

            var missing = new List<string>();

            if (string.IsNullOrWhiteSpace(request.SenderAddress)) missing.Add("endereço remetente");
            if (string.IsNullOrWhiteSpace(request.TenantId)) missing.Add("tenant");
            if (string.IsNullOrWhiteSpace(request.ClientId)) missing.Add("client id");
            if (!hasUsableSecret) missing.Add("client secret");

            if (missing.Count > 0)
            {
                return new UpdateEmailSettingsResult.Incomplete(missing);
            }
        }

        var changed = new List<string>();

        Set(settings.IsEnabled, request.IsEnabled, v => settings.IsEnabled = v, "IsEnabled", changed);
        Set(settings.SenderAddress, Clean(request.SenderAddress), v => settings.SenderAddress = v, "SenderAddress", changed);
        Set(settings.SenderName, Clean(request.SenderName), v => settings.SenderName = v, "SenderName", changed);
        Set(settings.TenantId, Clean(request.TenantId), v => settings.TenantId = v, "TenantId", changed);
        Set(settings.ClientId, Clean(request.ClientId), v => settings.ClientId = v, "ClientId", changed);
        Set(settings.ClientSecretExpiresOn, request.ClientSecretExpiresOn, v => settings.ClientSecretExpiresOn = v, "ClientSecretExpiresOn", changed);
        Set(settings.PortalUrl, Clean(request.PortalUrl)?.TrimEnd('/'), v => settings.PortalUrl = v, "PortalUrl", changed);

        if (newSecret is not null)
        {
            // Não dá para comparar com o anterior sem decifrá-lo, e não há motivo para
            // isso: quem mandou um secret quis trocar. A auditoria registra o nome do
            // campo, nunca o valor.
            settings.ProtectedClientSecret = protector.Protect(newSecret);
            settings.ClientSecretUpdatedAt = clock.UtcNow;
            changed.Add("ClientSecret");
        }

        if (changed.Count > 0)
        {
            settings.UpdatedById = actorId;

            db.SettingsAudit.Add(new SettingsAuditEntry
            {
                Area = AuditArea,
                ChangedFields = string.Join(",", changed),
                ChangedById = actorId
            });

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Configuração de e-mail alterada por {ActorId}: {Fields}.", actorId, string.Join(", ", changed));
        }

        return new UpdateEmailSettingsResult.Saved(await ViewAsync(settings, cancellationToken));
    }

    /// <summary>
    /// Envia agora, sem passar pela fila: o gestor quer a resposta na tela, e é ela que
    /// prova que a configuração gravada funciona. Usa a configuração salva, não a do
    /// formulário — testar o que ainda não foi gravado provaria outra coisa.
    /// </summary>
    public async Task<SendTestEmailResult> SendTestAsync(
        SendTestEmailRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await db.EmailSettings.SingleOrDefaultAsync(cancellationToken);

        if (LoadCredentials(settings) is not { } credentials)
        {
            return new SendTestEmailResult.NotConfigured(
                CredentialsProblem(settings) ?? "Configure o envio antes de testar.");
        }

        var message = new EmailMessage(
            request.ToAddress.Trim(),
            request.ToAddress.Trim(),
            "Teste de envio do OpsDesk",
            "<p>Se você recebeu esta mensagem, o OpsDesk consegue enviar e-mail por esta caixa.</p>");

        var result = await TrySendAsync(settings!, credentials, message, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return result is null ? new SendTestEmailResult.Sent() : new SendTestEmailResult.Failed(result);
    }

    /// <summary>Credenciais prontas para envio, ou nulo quando o envio não pode acontecer.</summary>
    internal EmailSenderCredentials? LoadCredentials(EmailSettings? settings)
    {
        if (settings is not { IsEnabled: true, SenderAddress: { } sender, TenantId: { } tenant, ClientId: { } client, ProtectedClientSecret: { } stored })
        {
            return null;
        }

        return protector.TryUnprotect(stored) is { } secret
            ? new EmailSenderCredentials(tenant, client, secret, sender, settings.SenderName)
            : null;
    }

    /// <summary>
    /// Envia e registra o resultado na configuração. Devolve a mensagem de erro, ou nulo
    /// em caso de sucesso. Quem chama grava.
    /// </summary>
    internal async Task<string?> TrySendAsync(
        EmailSettings settings,
        EmailSenderCredentials credentials,
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        settings.LastAttemptAt = clock.UtcNow;

        try
        {
            await sender.SendAsync(credentials, message, cancellationToken);

            settings.LastSucceededAt = settings.LastAttemptAt;
            settings.LastError = null;

            return null;
        }
        catch (EmailDeliveryException ex)
        {
            settings.LastError = Truncate(ex.Message, 2000);
            logger.LogWarning(ex, "Falha ao enviar e-mail para {To}.", message.ToAddress);

            return settings.LastError;
        }
    }

    private string? CredentialsProblem(EmailSettings? settings) => settings switch
    {
        null or { IsEnabled: false } => "O envio de e-mail está desligado.",
        { ProtectedClientSecret: { } stored } when protector.TryUnprotect(stored) is null =>
            "O client secret gravado não pode mais ser lido: a chave de cifragem do servidor mudou. Informe o secret de novo.",
        _ => null
    };

    private async Task<EmailSettingsView> ViewAsync(EmailSettings settings, CancellationToken cancellationToken)
    {
        var pending = await db.OutboundEmails.CountAsync(e => e.Status == OutboundEmailStatus.Pending, cancellationToken);
        var failed = await db.OutboundEmails.CountAsync(e => e.Status == OutboundEmailStatus.Failed, cancellationToken);

        return new EmailSettingsView(
            settings.IsEnabled,
            settings.SenderAddress,
            settings.SenderName,
            settings.TenantId,
            settings.ClientId,
            settings.ProtectedClientSecret is not null,
            settings.ProtectedClientSecret is { } stored && protector.TryUnprotect(stored) is not null,
            settings.ClientSecretUpdatedAt,
            settings.ClientSecretExpiresOn,
            settings.PortalUrl,
            settings.LastAttemptAt,
            settings.LastSucceededAt,
            settings.LastError,
            pending,
            failed);
    }

    private static void Set<T>(T current, T next, Action<T> assign, string field, List<string> changed)
    {
        if (!EqualityComparer<T>.Default.Equals(current, next))
        {
            assign(next);
            changed.Add(field);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
