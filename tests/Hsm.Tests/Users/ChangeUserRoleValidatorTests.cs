using Hsm.Application.Users.Commands.ChangeUserRole;
using Hsm.Domain.Identity;

namespace Hsm.Tests.Users;

public class ChangeUserRoleValidatorTests
{
    private static readonly ChangeUserRoleValidator Validator = new();

    [Fact]
    public void Missing_role_is_a_validation_failure_not_a_crash()
    {
        // Same RoleCatalog.IsKnown null-guard regression as
        // CreateStaffUserValidatorTests — this validator carries the same
        // .Must(RoleCatalog.IsKnown) rule.
        var command = new ChangeUserRoleCommand(Guid.NewGuid(), Role: null!);

        var result = Validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangeUserRoleCommand.Role));
    }

    [Fact]
    public void Known_role_is_valid()
    {
        var command = new ChangeUserRoleCommand(Guid.NewGuid(), Roles.Doctor);

        var result = Validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
