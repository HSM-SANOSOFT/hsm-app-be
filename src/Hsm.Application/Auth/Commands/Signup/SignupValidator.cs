using FluentValidation;

namespace Hsm.Application.Auth.Commands.Signup;

/// <summary>
/// Minimal stopgap (Task 3's fix round): only the two rules the deleted edge
/// BodyValidator enforced that a handler would otherwise silently accept a
/// broken value for — password length and email shape. Identity tasks
/// (11-14) reshape Auth entirely and own the rest of this command's
/// validation (username/name requiredness, etc.); do not extend this
/// validator ahead of that reshape.
/// </summary>
public sealed class SignupValidator : AbstractValidator<SignupCommand>
{
    public SignupValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}
