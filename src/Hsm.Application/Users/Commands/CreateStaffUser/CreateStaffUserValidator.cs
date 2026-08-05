using FluentValidation;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.CreateStaffUser;

/// <summary>
/// Shape and business rules for provisioning a staff account. The
/// patient/family exclusion is the one rule here that is not obvious: those
/// accounts are created through the patient registration path, never by an
/// admin provisioning staff, so handing one of those roles to this command is a
/// caller mistake and not a permission problem.
/// </summary>
public sealed class CreateStaffUserValidator : AbstractValidator<CreateStaffUserCommand>
{
    public CreateStaffUserValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FirstName).NotEmpty();
        RuleFor(x => x.FirstLastName).NotEmpty();
        RuleFor(x => x.TempPassword).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(RoleCatalog.IsKnown).WithMessage("role is not a known role.")
            .Must(RoleCatalog.IsAssignableToStaff)
                .WithMessage("role must be a staff role; patient accounts are created by registration.");
    }
}
