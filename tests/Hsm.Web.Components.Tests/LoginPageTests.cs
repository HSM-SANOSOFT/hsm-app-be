using Bunit;
using Hsm.Contracts.Ui;
using Hsm.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Web.Components.Tests;

/// <summary>
/// The sign-in screen (plan U18, screen 1): the form drives the
/// contracts-declared sign-in service, bad credentials render the error, and
/// success navigates to the return target.
/// </summary>
public sealed class LoginPageTests : MudTestContext
{
    [Fact]
    public void Renders_the_spanish_sign_in_form()
    {
        Services.AddSingleton<ISignInUiService>(new FakeSignInUiService(SignInResult.Success));

        var cut = Render<Login>();

        Assert.Contains("Iniciar sesión", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Nombre de usuario", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Contraseña", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("form"));
    }

    [Fact]
    public void Empty_submit_shows_a_validation_error_and_never_calls_the_service()
    {
        var fake = new FakeSignInUiService(SignInResult.Success);
        Services.AddSingleton<ISignInUiService>(fake);

        var cut = Render<Login>();
        cut.Find("form").Submit();

        Assert.Contains("Ingrese el nombre de usuario", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(fake.Attempts);
    }

    [Fact]
    public void Bad_credentials_render_the_error_from_the_service()
    {
        var fake = new FakeSignInUiService(SignInResult.Failed("Usuario o contraseña incorrectos."));
        Services.AddSingleton<ISignInUiService>(fake);

        var cut = Render<Login>();
        cut.Find("#username").Change("admin");
        cut.Find("#password").Change("wrong-password");
        cut.Find("form").Submit();

        Assert.Contains("Usuario o contraseña incorrectos.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal([("admin", "wrong-password")], fake.Attempts);
    }

    [Fact]
    public void Successful_sign_in_invokes_the_service_and_navigates_home()
    {
        var fake = new FakeSignInUiService(SignInResult.Success);
        Services.AddSingleton<ISignInUiService>(fake);

        var cut = Render<Login>();
        cut.Find("#username").Change("admin");
        cut.Find("#password").Change("correct-password");
        cut.Find("form").Submit();

        Assert.Equal([("admin", "correct-password")], fake.Attempts);
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.Equal(navigation.BaseUri, navigation.Uri);
    }
}
