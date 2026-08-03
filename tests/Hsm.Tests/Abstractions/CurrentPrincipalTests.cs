using Hsm.Application.Abstractions;

namespace Hsm.Tests.Abstractions;

public class CurrentPrincipalTests
{
    [Fact]
    public void Ambient_principal_returns_what_was_set()
    {
        var ambient = new AmbientPrincipal();
        Assert.Null(ambient.Actor);

        ambient.Set(new RequestActor("u1", ["admin"], OnboardingCompleted: true));

        Assert.Equal("u1", ambient.Actor!.Id);
        Assert.Contains("admin", ambient.Actor.Roles);
    }
}
