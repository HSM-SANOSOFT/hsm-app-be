using FluentValidation;

namespace Hsm.Application.Auth.Commands.CompleteOnboarding;

/// <summary>
/// Minimal stopgap (Task 3's fix round) — see <see cref="Hsm.Application.Auth.Commands.Signup.SignupValidator"/>
/// for why this is deliberately narrow. Identity tasks (11-14) own the rest.
/// </summary>
public sealed class CompleteOnboardingValidator : AbstractValidator<CompleteOnboardingCommand>
{
    public CompleteOnboardingValidator()
    {
        RuleFor(x => x.ConfirmEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}
