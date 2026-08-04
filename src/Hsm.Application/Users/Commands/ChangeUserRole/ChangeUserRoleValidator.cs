using FluentValidation;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.ChangeUserRole;

/// <summary>
/// The one shape rule for a role change: the new role must be one this system
/// knows about. Everything else about "may this actor make this change" is
/// authorization, decided by <see cref="Hsm.Application.Abstractions.RequireRoleAttribute"/>
/// on the command, not this validator.
/// </summary>
public sealed class ChangeUserRoleValidator : AbstractValidator<ChangeUserRoleCommand>
{
    public ChangeUserRoleValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(RoleCatalog.IsKnown).WithMessage("role is not a known role.");
    }
}
