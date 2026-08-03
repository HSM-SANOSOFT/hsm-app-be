using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Domain.Identity;

namespace Hsm.Tests.Users;

public class CreateStaffUserCommandTests
{
    [Fact]
    public void Is_admin_only()
    {
        var attribute = typeof(CreateStaffUserCommand).GetCustomAttribute<RequireRoleAttribute>();

        Assert.NotNull(attribute);
        Assert.Contains(Roles.Admin, attribute!.Roles);
    }
}
