using FluentValidation;

namespace OpsDesk.Application.Categories;

public class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator()
    {
        // Os limites são os da coluna (CategoryConfiguration). Estourar aqui dá mensagem;
        // estourar no banco daria 500.
        RuleFor(r => r.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Informe o nome da categoria.")
            .MaximumLength(100).WithMessage("O nome pode ter no máximo 100 caracteres.");

        RuleFor(r => r.Description)
            .MaximumLength(500).WithMessage("A descrição pode ter no máximo 500 caracteres.");
    }
}
