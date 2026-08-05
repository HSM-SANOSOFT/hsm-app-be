using FluentValidation;

namespace Hsm.Application.Users.Commands.UpdateOwnProfile;

/// <summary>
/// Both fields are optional (the caller may patch either or neither) but a
/// field that IS present must be well-formed — an explicit empty string is a
/// caller mistake, not "leave it alone".
/// </summary>
public sealed class UpdateOwnProfileValidator : AbstractValidator<UpdateOwnProfileCommand>
{
    public UpdateOwnProfileValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().When(x => x.FirstName is not null);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().When(x => x.Email is not null);
    }
}
