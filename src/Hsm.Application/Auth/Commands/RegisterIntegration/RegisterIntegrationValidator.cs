using FluentValidation;

namespace Hsm.Application.Auth.Commands.RegisterIntegration;

/// <summary>
/// All three fields are stored as non-nullable columns, so a body of <c>{}</c>
/// would otherwise reach the database and fail there — a 500 for what is plainly
/// a 400. Admin-gated, so the stakes are lower than the refresh route's, but the
/// hole is the same one.
///
/// <para>No rule on <c>Functionality</c>'s allowed VALUES: the shell checks its
/// own list before dispatching, and this command has no business being the
/// second, drifting copy of it.</para>
/// </summary>
public sealed class RegisterIntegrationValidator : AbstractValidator<RegisterIntegrationCommand>
{
    public RegisterIntegrationValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Description).NotEmpty();
        RuleFor(x => x.Functionality).NotEmpty();
    }
}
