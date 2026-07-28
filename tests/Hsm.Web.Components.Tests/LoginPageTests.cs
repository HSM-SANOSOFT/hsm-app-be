using Hsm.Web.Components.Pages;

namespace Hsm.Web.Components.Tests;

/// <summary>The U17 placeholder sign-in page: the redirect target must render
/// (U18 replaces the placeholder with the working screen).</summary>
public sealed class LoginPageTests : MudTestContext
{
    [Fact]
    public void Renders_the_spanish_sign_in_placeholder()
    {
        var cut = Render<Login>();

        Assert.Contains("Iniciar sesión", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Nombre de usuario", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Contraseña", cut.Markup, StringComparison.Ordinal);
    }
}
