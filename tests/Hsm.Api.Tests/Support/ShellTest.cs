using System.Net.Http.Json;

namespace Hsm.Api.Tests;

/// <summary>
/// Base for the three shell suites carried over from the retired
/// frozen-contract tests: schema readiness, a cookie-transparent client, and a
/// sign-in helper.
///
/// <para>Task 12 moved that helper onto the Identity session cookie. It signs
/// in at the REST door (<c>POST /api/v1/identity/login</c>, routed to the
/// sidecar) and hands back the cookie the SHELL is then asked to recognise —
/// which is the property the whole two-host topology exists to prove, and it
/// only holds because both hosts share one data-protection key ring.</para>
/// </summary>
public abstract class ShellTest<TFactory>(TFactory factory) : IAsyncLifetime
    where TFactory : ShellFactory
{
    /// <summary>The session cookie both hosts issue and read.</summary>
    protected const string SessionCookieName = "hsm.session";

    protected const string SeedPassword = "Contract-Passw0rd";

    protected TFactory Factory { get; } = factory;

    protected HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Factory.EnsureSchemaAsync();
        Client = Factory.CreateApiClient();
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    protected static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    /// <summary>Seeds a user with <paramref name="role"/>, signs it in, and
    /// returns its <c>name=value</c> session cookie with the user id.</summary>
    protected async Task<(string SessionCookie, Guid UserId)> SessionAsync(
        string role = "doctor", bool onboarded = true)
    {
        var username = Unique("user");
        var id = await Factory.SeedUserAsync(
            username, SeedPassword, role, onboarded ? DateTimeOffset.UtcNow : null);
        return (await SignInAsync(username, SeedPassword), id);
    }

    /// <summary>Signs in an existing account and returns its session cookie.</summary>
    protected async Task<string> SignInAsync(string username, string password)
    {
        using var response = await Client.PostAsJsonAsync(
            new Uri("/api/v1/identity/login", UriKind.Relative),
            new { username, password },
            CancellationToken.None);
        Assert.True(
            response.IsSuccessStatusCode,
            $"login failed: {(int)response.StatusCode} "
                + await response.Content.ReadAsStringAsync(CancellationToken.None));
        return SessionCookieOf(response);
    }

    /// <summary>The <c>name=value</c> session cookie a response set, asserting it set one.</summary>
    protected static string SessionCookieOf(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
        var cookie = Assert.Single(
            setCookies,
            value => value.StartsWith($"{SessionCookieName}=", StringComparison.Ordinal));
        return cookie.Split(';', 2)[0];
    }
}
