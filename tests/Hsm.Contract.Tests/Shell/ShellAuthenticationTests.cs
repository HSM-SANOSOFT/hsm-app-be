using System.Net;

namespace Hsm.Contract.Tests.Shell;

/// <summary>
/// The Blazor shell's session behavior end to end through the real host
/// (plan U17, DoD C6): an anonymous visitor asking for a protected shell page
/// is redirected to sign-in, the sign-in placeholder renders anonymously, and
/// a session signed in through the REST login cookie reaches the shell.
/// </summary>
public sealed class ShellFactory : ContractApiFactory
{
    protected override string DatabaseName => "hsm_shell_test";
}

public sealed class ShellAuthenticationTests(ShellFactory factory)
    : ContractTest<ShellFactory>(factory), IClassFixture<ShellFactory>
{
    [Fact]
    public async Task Anonymous_visitor_to_the_shell_is_redirected_to_login()
    {
        using var response = await Client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        Assert.StartsWith("/login", location, StringComparison.Ordinal);
        Assert.Contains("returnUrl=", location, StringComparison.Ordinal);
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
        // access-token cookie the shell's authentication scheme validates.
        var (bearer, _) = await BearerAsync(role: "admin");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", $"access_token={bearer}");
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
        var (bearer, _) = await BearerAsync(role: "doctor");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", $"access_token={bearer}");
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Inicio", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Usuarios y roles", body, StringComparison.Ordinal);
    }
}
