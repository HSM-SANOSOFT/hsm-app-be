using FluentValidation;

namespace Hsm.Application.Users.Commands.ChangeOwnPassword;

/// <summary>Self-service password change: the current password is presented (not judged) and the new one meets the minimum length requirement.</summary>
public sealed class ChangeOwnPasswordValidator : AbstractValidator<ChangeOwnPasswordCommand>
{
    public ChangeOwnPasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}
