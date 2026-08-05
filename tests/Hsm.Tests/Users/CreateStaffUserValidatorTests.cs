using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Domain.Identity;

namespace Hsm.Tests.Users;

public class CreateStaffUserValidatorTests
{
    private static readonly CreateStaffUserValidator Validator = new();

    [Fact]
    public void Missing_role_is_a_validation_failure_not_a_crash()
    {
        // Regression: a body-bound command's Role is a real null (not
        // string.Empty) when the caller omits the field, and FluentValidation's
        // default cascade (Continue) runs every Must rule on a property even
        // after an earlier NotEmpty on that same property has already failed —
        // so RoleCatalog.IsKnown must tolerate null rather than throwing.
        var command = new CreateStaffUserCommand(
            "user1", "user1@test.local", "First", null, "Last", null, null,
            Role: null!, "Temp-Passw0rd");

        var result = Validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateStaffUserCommand.Role));
    }

    [Fact]
    public void Known_staff_role_is_valid()
    {
        var command = new CreateStaffUserCommand(
            "user1", "user1@test.local", "First", null, "Last", null, null,
            Roles.Nurse, "Temp-Passw0rd");

        var result = Validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
