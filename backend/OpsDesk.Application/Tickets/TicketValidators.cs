using FluentValidation;

namespace OpsDesk.Application.Tickets;

public class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(r => r.Title)
            .NotEmpty().WithMessage("Informe um título.")
            .MaximumLength(200).WithMessage("O título deve ter no máximo 200 caracteres.");

        RuleFor(r => r.Description)
            .NotEmpty().WithMessage("Descreva o problema.")
            // Sem limite superior: o solicitante costuma colar log e mensagem de erro, e
            // cortar isso só faria a equipe pedir o resto no primeiro comentário.
            .MinimumLength(10).WithMessage("Descreva o problema com pelo menos 10 caracteres.");

        RuleFor(r => r.CategoryId)
            .NotEmpty().WithMessage("Escolha uma categoria.");

        RuleFor(r => r.Priority)
            .IsInEnum().WithMessage("Prioridade inválida.");
    }
}
