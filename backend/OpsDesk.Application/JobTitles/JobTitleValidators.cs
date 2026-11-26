using FluentValidation;

namespace OpsDesk.Application.JobTitles;

public class SaveJobTitleRequestValidator : AbstractValidator<SaveJobTitleRequest>
{
    public SaveJobTitleRequestValidator()
    {
        // O limite é o da coluna (JobTitleConfiguration).
        RuleFor(r => r.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Informe o nome do cargo.")
            .MaximumLength(100).WithMessage("O nome pode ter no máximo 100 caracteres.");
    }
}
