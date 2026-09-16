using FluentValidation;

namespace OpsDesk.Application.Tickets;

public class AddCommentRequestValidator : AbstractValidator<AddCommentRequest>
{
    public AddCommentRequestValidator()
    {
        RuleFor(r => r.Content)
            .NotEmpty().WithMessage("Escreva o comentário.")
            .MaximumLength(10_000).WithMessage("O comentário está muito longo.");
    }
}

public class ChangeStatusRequestValidator : AbstractValidator<ChangeStatusRequest>
{
    public ChangeStatusRequestValidator()
    {
        // Só o formato. Se a transição é permitida é decisão da máquina de estados, que
        // depende do status atual do chamado e do perfil de quem pediu — coisas que um
        // validator de corpo não conhece.
        RuleFor(r => r.Status).IsInEnum().WithMessage("Status inválido.");
    }
}
