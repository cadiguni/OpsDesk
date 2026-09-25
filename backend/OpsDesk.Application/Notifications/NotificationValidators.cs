using FluentValidation;

namespace OpsDesk.Application.Notifications;

public class UpdateEmailSettingsRequestValidator : AbstractValidator<UpdateEmailSettingsRequest>
{
    public UpdateEmailSettingsRequestValidator()
    {
        // Os limites são os das colunas (EmailSettingsConfiguration): estourar aqui dá
        // mensagem, estourar no banco daria 500. O que falta para ligar o envio é decidido
        // no serviço, que sabe se já existe secret gravado.
        RuleFor(r => r.SenderAddress)
            .EmailAddress().WithMessage("Endereço remetente inválido.")
            .MaximumLength(320)
            .When(r => !string.IsNullOrWhiteSpace(r.SenderAddress));

        RuleFor(r => r.SenderName).MaximumLength(200).WithMessage("Nome de exibição muito longo.");
        RuleFor(r => r.TenantId).MaximumLength(100).WithMessage("Tenant muito longo.");

        RuleFor(r => r.ClientId)
            .Must(id => Guid.TryParse(id, out _)).WithMessage("O client id é um GUID, como o Entra ID o mostra.")
            .When(r => !string.IsNullOrWhiteSpace(r.ClientId));

        RuleFor(r => r.ClientSecret).MaximumLength(500).WithMessage("Client secret muito longo.");

        RuleFor(r => r.PortalUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                         && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            .WithMessage("Informe o endereço completo do portal, com http:// ou https://.")
            .MaximumLength(500)
            .When(r => !string.IsNullOrWhiteSpace(r.PortalUrl));
    }
}

public class SendTestEmailRequestValidator : AbstractValidator<SendTestEmailRequest>
{
    public SendTestEmailRequestValidator()
    {
        RuleFor(r => r.ToAddress)
            .NotEmpty().WithMessage("Informe para quem enviar o teste.")
            .EmailAddress().WithMessage("Endereço inválido.");
    }
}
