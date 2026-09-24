using FluentValidation;

namespace OpsDesk.Application.Users;

public class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator()
    {
        // Só o formato. Se a troca é permitida — não é a própria conta, não deixa chamado
        // órfão — depende do banco, e é o serviço que decide.
        RuleFor(r => r.Role).IsInEnum().WithMessage("Perfil inválido.");
    }
}
