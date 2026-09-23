using FluentValidation;

namespace OpsDesk.Application.Auth;

/// <summary>
/// Regras de senha da versão 1.
///
/// Mínimo de oito caracteres e nada além disso. Exigir maiúscula, número e símbolo é o
/// padrão que empurra as pessoas para "Senha@123" e para o post-it no monitor; comprimento
/// é o fator que realmente pesa. A defesa contra força bruta neste projeto é o rate
/// limiting no login mais o PBKDF2 do PasswordHasher, não a composição do texto.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public const int MaximumLength = 128;
}

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Informe seu nome.")
            .MaximumLength(200).WithMessage("O nome deve ter no máximo 200 caracteres.");

        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Informe seu e-mail.")
            .MaximumLength(320).WithMessage("O e-mail deve ter no máximo 320 caracteres.")
            .EmailAddress().WithMessage("Informe um e-mail válido.");

        RuleFor(r => r.Password)
            .NotEmpty().WithMessage("Informe uma senha.")
            .MinimumLength(PasswordPolicy.MinimumLength)
                .WithMessage($"A senha deve ter no mínimo {PasswordPolicy.MinimumLength} caracteres.")
            .MaximumLength(PasswordPolicy.MaximumLength)
                .WithMessage($"A senha deve ter no máximo {PasswordPolicy.MaximumLength} caracteres.");

        RuleFor(r => r.PasswordConfirmation)
            .Equal(r => r.Password).WithMessage("As senhas não conferem.");
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Sem regra de formato nem de comprimento aqui de propósito: a resposta do login é
        // sempre a mesma mensagem genérica, e validar o formato do e-mail antes de comparar
        // a senha criaria dois tempos de resposta distintos.
        RuleFor(r => r.Email).NotEmpty().WithMessage("Informe seu e-mail.");
        RuleFor(r => r.Password).NotEmpty().WithMessage("Informe sua senha.");
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword)
            .NotEmpty().WithMessage("Informe sua senha atual.");

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
