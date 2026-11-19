using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Auth;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Application.Settings;

/// <summary>Configuração geral, como o gestor a vê e edita.</summary>
/// <param name="MicrosoftSignInReady">
/// O aplicativo do Entra ID da configuração de e-mail está completo e com secret legível. Ligado
/// sem isso, o botão não aparece no login — a tela de configuração avisa.
/// </param>
public record GeneralSettings(
    string OrganizationName,
    bool AllowSelfRegistration,
    IReadOnlyList<string> AllowedEmailDomains,
    bool MicrosoftSignInEnabled,
    bool MicrosoftSignInReady);

public record UpdateGeneralSettingsRequest(
    string? OrganizationName,
    bool AllowSelfRegistration,
    IReadOnlyList<string>? AllowedEmailDomains,
    bool MicrosoftSignInEnabled = false);

/// <summary>
/// O que a tela de login precisa saber antes de existir sessão. Público de propósito, e
/// por isso enxuto: nada aqui ajuda quem quer descobrir quem tem conta.
/// </summary>
/// <param name="PasswordResetAvailable">
/// O envio de e-mail está ligado e sabe o endereço do portal — sem isso o link de
/// redefinição não chegaria, e oferecer "esqueci minha senha" seria prometer o que não sai.
/// </param>
public record PublicSettings(
    string OrganizationName,
    bool AllowSelfRegistration,
    IReadOnlyList<string> AllowedEmailDomains,
    bool PasswordResetAvailable,
    bool MicrosoftSignInAvailable);

public class UpdateGeneralSettingsRequestValidator : AbstractValidator<UpdateGeneralSettingsRequest>
{
    public UpdateGeneralSettingsRequestValidator()
    {
        RuleFor(r => r.OrganizationName).MaximumLength(100).WithMessage("O nome pode ter no máximo 100 caracteres.");

        RuleForEach(r => r.AllowedEmailDomains)
            .Must(SystemSettingsService.IsDomain)
            .WithMessage("Domínio inválido: '{PropertyValue}'. Use só o domínio, como empresa.com.br.");
    }
}

/// <summary>
/// Configuração geral do sistema (README, seção 13.12): nome da organização e política de
/// autocadastro.
/// </summary>
public class SystemSettingsService(IOpsDeskDbContext db, MicrosoftSignInSettings microsoft)
{
    public const string AuditArea = "General";

    public async Task<GeneralSettings> GetAsync(CancellationToken cancellationToken = default) =>
        await ViewAsync(
            await db.SystemSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new SystemSettings(),
            cancellationToken);

    public async Task<PublicSettings> GetPublicAsync(CancellationToken cancellationToken = default)
    {
        var general = await GetAsync(cancellationToken);

        var resetAvailable = await db.EmailSettings
            .AsNoTracking()
            .AnyAsync(s => s.IsEnabled && s.PortalUrl != null, cancellationToken);

        return new PublicSettings(
            general.OrganizationName,
            general.AllowSelfRegistration,
            general.AllowedEmailDomains,
            resetAvailable,
            general is { MicrosoftSignInEnabled: true, MicrosoftSignInReady: true });
    }

    public async Task<GeneralSettings> UpdateAsync(
        UpdateGeneralSettingsRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        var settings = await db.SystemSettings.SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new SystemSettings();
            db.SystemSettings.Add(settings);
        }

        var name = string.IsNullOrWhiteSpace(request.OrganizationName) ? null : request.OrganizationName.Trim();
        var domains = NormalizeDomains(request.AllowedEmailDomains);

        var changed = new List<string>();

        if (settings.OrganizationName != name)
        {
            settings.OrganizationName = name;
            changed.Add("OrganizationName");
        }

        if (settings.AllowSelfRegistration != request.AllowSelfRegistration)
        {
            settings.AllowSelfRegistration = request.AllowSelfRegistration;
            changed.Add("AllowSelfRegistration");
        }

        if (settings.AllowedEmailDomains != domains)
        {
            settings.AllowedEmailDomains = domains;
            changed.Add("AllowedEmailDomains");
        }

        if (settings.MicrosoftSignInEnabled != request.MicrosoftSignInEnabled)
        {
            settings.MicrosoftSignInEnabled = request.MicrosoftSignInEnabled;
            changed.Add("MicrosoftSignInEnabled");
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
        }

        return await ViewAsync(settings, cancellationToken);
    }

    /// <summary>
    /// O autocadastro aceita este e-mail? Compara o domínio exato, e não sufixo: liberar
    /// <c>empresa.com</c> não libera <c>golpe-empresa.com</c>.
    /// </summary>
    public static bool AcceptsEmail(SystemSettings? settings, string email)
    {
        var domains = Split(settings?.AllowedEmailDomains);

        if (domains.Count == 0)
        {
            return true;
        }

        var at = email.LastIndexOf('@');

        return at >= 0 && domains.Contains(email[(at + 1)..].Trim().ToLowerInvariant());
    }

    internal static bool IsDomain(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.CheckHostName(value.Trim().TrimStart('@')) == UriHostNameType.Dns
        && value.Trim().TrimStart('@').Contains('.');

    private static string? NormalizeDomains(IEnumerable<string>? domains)
    {
        var normalized = (domains ?? [])
            .Select(d => d.Trim().TrimStart('@').ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        return normalized.Count == 0 ? null : string.Join(",", normalized);
    }

    private static List<string> Split(string? domains) =>
        (domains ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Domínios aceitos no autocadastro, já separados.</summary>
    public static IReadOnlyList<string> DomainsOf(SystemSettings? settings) => Split(settings?.AllowedEmailDomains);

    private async Task<GeneralSettings> ViewAsync(SystemSettings s, CancellationToken cancellationToken) => new(
        s.OrganizationName ?? SystemSettings.DefaultOrganizationName,
        s.AllowSelfRegistration,
        Split(s.AllowedEmailDomains),
        s.MicrosoftSignInEnabled,
        await microsoft.IsAppReadyAsync(cancellationToken));
}
