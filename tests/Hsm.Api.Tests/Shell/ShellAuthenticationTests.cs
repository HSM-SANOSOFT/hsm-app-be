using System.Net;

namespace Hsm.Api.Tests.Shell;

/// <summary>
/// The Blazor shell's session behavior end to end through the real host
/// (plan U17, DoD C6): an anonymous visitor asking for a protected shell page
/// is redirected to sign-in, the sign-in placeholder renders anonymously, and
/// a session signed in at the REST door reaches the shell. Since Task 12 that
/// last one is the ONE Identity session cookie, decrypted by the other host
/// off the shared data-protection key ring — the strongest form of "one
/// sign-in serves both doors" this topology can state.
/// </summary>
public sealed class ShellAuthenticationFactory : ShellFactory
{
    protected override string DatabaseName => "hsm_shell_test";
}

public sealed class ShellAuthenticationTests(ShellAuthenticationFactory factory)
    : ShellTest<ShellAuthenticationFactory>(factory), IClassFixture<ShellAuthenticationFactory>
{
    [Fact]
    public async Task Anonymous_visitor_to_the_shell_is_redirected_to_login()
    {
        using var response = await Client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        // The cookie handler builds an absolute redirect (the hand-rolled one
        // it replaced wrote a relative path); the PATH and the return URL are
        // what this pins.
        var location = response.Headers.Location ?? throw new InvalidOperationException("no Location");
        Assert.Equal("/login", location.AbsolutePath);
        Assert.Contains("returnUrl=", location.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_placeholder_page_renders_anonymously()
    {
        using var response = await Client.GetAsync(new Uri("/login", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Iniciar sesión", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_admin_reaches_the_shell_with_admin_navigation()
    {
        // Sign in exactly the way a browser does: REST login issues the
        // session cookie the shell's authentication scheme validates.
        var (session, _) = await SessionAsync(role: "admin");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", session);
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // The shell rendered (not the login page), and the role-aware nav
        // reflects the admin session in the prerendered markup.
        Assert.Contains("Inicio", body, StringComparison.Ordinal);
        Assert.Contains("Administración", body, StringComparison.Ordinal);
        Assert.DoesNotContain("El inicio de sesión desde esta pantalla", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_non_admin_reaches_the_shell_without_admin_navigation()
    {
        var (session, _) = await SessionAsync(role: "doctor");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", session);
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Inicio", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Usuarios y roles", body, StringComparison.Ordinal);
    }
}
