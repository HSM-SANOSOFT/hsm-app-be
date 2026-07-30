using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Application.Settings.Commands.UpdateSettings;
using Hsm.Domain.Identity;

namespace Hsm.Tests.Settings;

public class UpdateSettingsCommandTests
{
    [Fact]
    public void Is_admin_only()
    {
        var attribute = typeof(UpdateSettingsCommand).GetCustomAttribute<RequireRoleAttribute>();

        Assert.NotNull(attribute);
        Assert.Contains(Roles.Admin, attribute!.Roles);
    }
}
