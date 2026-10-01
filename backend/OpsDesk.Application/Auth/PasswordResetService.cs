using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Auth;

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword, string NewPasswordConfirmation);

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().WithMessage("Informe seu e-mail.");
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.Token).NotEmpty().WithMessage("Link inválido.");

        RuleFor(r => r.NewPassword)
            .NotEmpty().WithMessage("Informe a nova senha.")
            .MinimumLength(PasswordPolicy.MinimumLength)
                .WithMessage($"A senha deve ter no mínimo {PasswordPolicy.MinimumLength} caracteres.")
            .MaximumLength(PasswordPolicy.MaximumLength)
                .WithMessage($"A senha deve ter no máximo {PasswordPolicy.MaximumLength} caracteres.");

        RuleFor(r => r.NewPasswordConfirmation)
            .Equal(r => r.NewPassword).WithMessage("As senhas não conferem.");
    }
}

public enum ResetPasswordResult
{
    Reset,

    /// <summary>Desconhecido, usado, expirado, ou de conta desativada — sempre a mesma resposta.</summary>
    InvalidLink
}

/// <summary>
/// Links de "definir senha", por e-mail: a redefinição de quem esqueceu a senha, e o
/// convite de quem foi cadastrado pelo gestor (README, seção 13.13).
///
/// Três cuidados:
///
/// <list type="bullet">
/// <item>o pedido responde igual exista ou não a conta, para "esqueci minha senha" não
/// virar consulta de quem trabalha na empresa;</item>
/// <item>só o hash do token fica no banco, e o e-mail que carrega o link é marcado como
/// sensível — o corpo some da fila de saída depois do envio;</item>
/// <item>o link vale uma vez, por pouco tempo, e o pedido novo invalida o anterior.</item>
/// </list>
/// </summary>
public class PasswordResetService(
    IOpsDeskDbContext db,
    IPasswordHasher<User> passwordHasher,
    IRefreshTokenStore refreshTokens,
    IClock clock,
    ILogger<PasswordResetService> logger)
{
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    /// <summary>Convite dura mais: a pessoa nova pode só abrir o e-mail no dia seguinte.</summary>
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(3);

    /// <summary>
    /// Pede o link de redefinição. Não diz se a conta existe: quem chama recebe a mesma
    /// resposta em todos os casos, e só a pessoa dona do e-mail fica sabendo.
    /// </summary>
    public async Task RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);
        var channel = await ChannelAsync(cancellationToken);

        if (user is null || channel is null)
        {
            // Nada a fazer, e nada a dizer. O log registra para quem administra, sem o
            // e-mail inteiro: log também vaza.
            logger.LogInformation("Pedido de redefinição sem envio (conta inexistente, inativa ou e-mail desligado).");
            return;
        }

        await IssueAsync(user, channel, isInvitation: false, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Convite para quem o gestor acabou de cadastrar. Falso quando o envio de e-mail não
    /// está disponível — aí quem chama recorre à senha provisória.
    /// </summary>
    public async Task<bool> InviteAsync(User user, CancellationToken cancellationToken = default)
    {
        if (await ChannelAsync(cancellationToken) is not { } channel)
        {
            return false;
        }

        await IssueAsync(user, channel, isInvitation: true, cancellationToken);

        return true;
    }

    /// <summary>O envio de e-mail está pronto para mandar link? Ligado e com endereço do portal.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        await ChannelAsync(cancellationToken) is not null;

    public async Task<ResetPasswordResult> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var hash = Hash(request.Token);
        var now = clock.UtcNow;

        var token = await db.PasswordResetTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null || token.UsedAt is not null || token.ExpiresAt <= now || !token.User.IsActive)
        {
            return ResetPasswordResult.InvalidLink;
        }

        var user = token.User;

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.PasswordChangeReason = UserAuditAction.PasswordSetByLink;

        // A senha agora é escolha da pessoa: se havia provisória, ela caiu.
        user.MustChangePassword = false;
        token.UsedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        // Quem pede redefinição pode estar desconfiando de acesso alheio: as sessões abertas
        // caem, como na troca de senha.
        await refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);

        logger.LogInformation("Senha de {UserId} definida pelo link.", user.Id);

        return ResetPasswordResult.Reset;
    }

    private async Task IssueAsync(User user, ResetChannel channel, bool isInvitation, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Pedido novo invalida os anteriores: o último e-mail recebido é o único que vale.
        await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.UsedAt, now), cancellationToken);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = Hash(token),
            ExpiresAt = now + (isInvitation ? InvitationLifetime : ResetLifetime),
            CreatedAt = now
        });

        var link = $"{channel.PortalUrl}/redefinir-senha?token={Uri.EscapeDataString(token)}";

        db.OutboundEmails.Add(new OutboundEmail
        {
            ToAddress = user.Email,
            ToName = user.Name,
            Subject = isInvitation
                ? $"Sua conta no {channel.OrganizationName}"
                : $"Redefinição de senha do {channel.OrganizationName}",
            HtmlBody = Body(user.Name, channel.OrganizationName, link, isInvitation),
            IsSensitive = true,
            Status = OutboundEmailStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now
        });
    }

    private static string Body(string name, string organization, string link, bool isInvitation)
    {
        var html = new StringBuilder();

        html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1f2937\">");
        html.Append($"<p>Olá, {WebUtility.HtmlEncode(name)}.</p>");

        html.Append(isInvitation
            ? $"<p>Uma conta foi criada para você no {WebUtility.HtmlEncode(organization)}, a central de chamados de TI. " +
              "Para começar, defina sua senha:</p>"
            : "<p>Recebemos um pedido para redefinir a sua senha. Se foi você, use o link abaixo:</p>");

        html.Append($"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Definir minha senha</a></p>");

        html.Append(isInvitation
            ? "<p style=\"color:#6b7280;font-size:12px\">O link vale por três dias e funciona uma única vez.</p>"
            : "<p style=\"color:#6b7280;font-size:12px\">O link vale por uma hora e funciona uma única vez. " +
              "Se você não pediu, ignore este e-mail: sua senha continua a mesma.</p>");

        html.Append("</div>");

        return html.ToString();
    }

    private sealed record ResetChannel(string PortalUrl, string OrganizationName);

    private async Task<ResetChannel?> ChannelAsync(CancellationToken cancellationToken)
    {
        var email = await db.EmailSettings
            .AsNoTracking()
            .Where(s => s.IsEnabled && s.PortalUrl != null)
            .Select(s => s.PortalUrl)
            .SingleOrDefaultAsync(cancellationToken);

        if (email is null)
        {
            return null;
        }

        var organization = await db.SystemSettings.AsNoTracking()
            .Select(s => s.OrganizationName)
            .SingleOrDefaultAsync(cancellationToken);

        return new ResetChannel(email.TrimEnd('/'), organization ?? SystemSettings.DefaultOrganizationName);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
