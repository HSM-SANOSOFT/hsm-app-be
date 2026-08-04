using FluentValidation;

namespace Hsm.Application.Auth.Commands.ResetPassword;

/// <summary>
/// Minimal stopgap (Task 3's fix round) — see <see cref="Hsm.Application.Auth.Commands.Signup.SignupValidator"/>
/// for why this is deliberately narrow (no email field on this command, so
/// only the password rule applies). Identity tasks (11-14) own the rest.
/// </summary>
public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}
