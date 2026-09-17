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

public class ChangeClassificationRequestValidator : AbstractValidator<ChangeClassificationRequest>
{
    public ChangeClassificationRequestValidator()
    {
        // Pedido que não muda nada é erro de cliente, não operação silenciosa: sem isto,
        // um corpo vazio responderia 200 sem ter feito coisa alguma.
        RuleFor(r => r)
            .Must(r => r.Priority is not null || r.CategoryId is not null)
            .WithMessage("Informe a prioridade, a categoria, ou as duas.");

        RuleFor(r => r.Priority)
            .IsInEnum().WithMessage("Prioridade inválida.")
            .When(r => r.Priority is not null);

        RuleFor(r => r.CategoryId)
            .NotEmpty().WithMessage("Escolha uma categoria.")
            .When(r => r.CategoryId is not null);
    }
}
