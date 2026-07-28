using Hsm.Contracts.Ui;
using Hsm.Web.Components.Layout;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Web.Components.Tests;

/// <summary>Role-aware navigation (plan U17): items render per role, resolved
/// through the contracts-declared identity service.</summary>
public sealed class NavMenuTests : MudTestContext
{
    [Fact]
    public void Admin_sees_the_administration_items()
    {
        Services.AddSingleton<ICurrentUserUiService>(FakeCurrentUserUiService.WithRoles(UiRoles.Admin));

        var cut = Render<NavMenu>();

        Assert.Contains("Administración", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Usuarios y roles", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Cuentas de integración", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Configuración", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Documentos", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_admin_gets_only_the_common_items()
    {
        Services.AddSingleton<ICurrentUserUiService>(FakeCurrentUserUiService.WithRoles("doctor"));

        var cut = Render<NavMenu>();

        Assert.Contains("Inicio", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Administración", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Usuarios y roles", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Anonymous_session_renders_no_admin_navigation()
    {
        Services.AddSingleton<ICurrentUserUiService>(FakeCurrentUserUiService.Anonymous);

        var cut = Render<NavMenu>();

        Assert.Contains("Inicio", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Administración", cut.Markup, StringComparison.Ordinal);
    }
}
