using FluentValidation;

namespace OpsDesk.Application.Users;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Informe o nome.")
            .MaximumLength(200).WithMessage("O nome deve ter no máximo 200 caracteres.");

        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Informe o e-mail.")
            .MaximumLength(320).WithMessage("O e-mail deve ter no máximo 320 caracteres.")
            .EmailAddress().WithMessage("Informe um e-mail válido.");

        RuleFor(r => r.Role).IsInEnum().WithMessage("Perfil inválido.");

        // Se o cargo existe e está ativo depende do banco; quem decide é o serviço.
        RuleFor(r => r.JobTitleId).NotEqual(Guid.Empty).WithMessage("Cargo inválido.");
    }
}

public class ChangeJobTitleRequestValidator : AbstractValidator<ChangeJobTitleRequest>
{
    public ChangeJobTitleRequestValidator()
    {
        RuleFor(r => r.JobTitleId).NotEqual(Guid.Empty).WithMessage("Cargo inválido.");
    }
}

public class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator()
    {
        // Só o formato. Se a troca é permitida — não é a própria conta, não deixa chamado
        // órfão — depende do banco, e é o serviço que decide.
        RuleFor(r => r.Role).IsInEnum().WithMessage("Perfil inválido.");
    }
}
