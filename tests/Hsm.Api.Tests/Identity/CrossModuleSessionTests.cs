using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Domain.Identity;
using Hsm.Domain.Settings;

namespace Hsm.Api.Tests.Identity;

public sealed class CrossModuleSessionFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_cross_module_session";
}

/// <summary>
/// One sign-in, one antiforgery token, four modules, one sign-out.
///
/// <para>Every other suite in this assembly boots its own factory against its
/// own database and exercises one module through it — which means the
/// middleware stack every module shares (<c>UseHsmActor</c>,
/// <c>HsmAntiforgery</c>, the cookie's <c>OnValidatePrincipal</c> chain) is only
/// ever proven inside one module's fixture at a time. A change that made a
/// session work for Documents and not for Settings, or an antiforgery token
/// bound in a way that only round-trips within the endpoint group that issued
/// it, would leave all of them green.</para>
///
/// <para>This test is the composition itself: the credentials are obtained ONCE,
/// by hand, and then reused across a read in one module, a mutation in a
/// second, and a read in a third, before a single sign-out has to invalidate
/// all of it.</para>
/// </summary>
public class CrossModuleSessionTests(CrossModuleSessionFactory factory)
    : IClassFixture<CrossModuleSessionFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task One_session_and_one_csrf_token_serve_every_module_until_sign_out()
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        await factory.SeedUserAsync(
            username, CrossModuleSessionFactory.SeedPassword, Roles.Admin, DateTimeOffset.UtcNow);
        using var client = factory.CreateApiClient();

        // ----- sign in ONCE ------------------------------------------------
        var login = await client.PostAsJsonAsync(
            "/api/v1/identity/login",
            new { username, password = CrossModuleSessionFactory.SeedPassword },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        Accept(cookies, login);
        SetCookieHeader(client, cookies);

        // ----- fetch the antiforgery token ONCE ----------------------------
        var csrf = await client.GetAsync(
            new Uri("/api/v1/identity/csrf", UriKind.Relative), CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        Accept(cookies, csrf);
        SetCookieHeader(client, cookies);
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None))
            .GetProperty("token").GetString();
        client.DefaultRequestHeaders.Add(CrossModuleSessionFactory.AntiforgeryHeader, token);

        // ----- module 1: a read in Documents -------------------------------
        var documents = await client.GetAsync("/api/v1/documents?pageSize=1", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, documents.StatusCode);

        // ----- module 2: a MUTATION in Settings, on that same token ---------
        // The point of doing the mutation in a different module from the read:
        // HsmAntiforgery is middleware, not per-group configuration, and a token
        // that only validated within the group that issued it would fail exactly
        // here.
        var value = $"smtp-{Guid.NewGuid():N}.example.test";
        var settings = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new
            {
                category = SettingsCategories.Email,
                settings = new[] { new { key = "SMTP_ADDRESS", value } },
            },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);

        // ----- module 3: a mutation in Users, still the same token ----------
        var password = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { currentPassword = CrossModuleSessionFactory.SeedPassword, newPassword = "New-Passw0rd9" },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, password.StatusCode);

        // The password change rotated the security stamp, which reissues THIS
        // session's cookie rather than ending it (see UserEndpoints'
        // ChangeOwnPassword). Take the replacement the way a browser would —
        // not doing so would make the rest of this test measure the cookie jar
        // rather than the session.
        Accept(cookies, password);
        SetCookieHeader(client, cookies);

        // ----- module 4: a read in Templates -------------------------------
        var templates = await client.GetAsync("/api/v1/templates", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, templates.StatusCode);

        // ----- sign out ONCE ------------------------------------------------
        var logout = await client.PostAsync(
            new Uri("/api/v1/identity/logout", UriKind.Relative), content: null, CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // ----- and now NOTHING authenticates -------------------------------
        // Same cookie, same antiforgery token, still sent. Every module refuses,
        // because the SESSION is gone — not because the client forgot anything.
        await ProblemAssert.ProblemAsync(
            await client.GetAsync("/api/v1/documents?pageSize=1", CancellationToken.None), 401);
        await ProblemAssert.ProblemAsync(
            await client.GetAsync("/api/v1/templates", CancellationToken.None), 401);
        await ProblemAssert.ProblemAsync(
            await client.GetAsync($"/api/v1/settings?category={SettingsCategories.Email}", CancellationToken.None),
            401);
        await ProblemAssert.ProblemAsync(
            await client.GetAsync("/api/v1/identity/me", CancellationToken.None), 401);
    }

    /// <summary>Last value per NAME wins, the way a browser's jar resolves it.</summary>
    private static void Accept(Dictionary<string, string> jar, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return;
        }

        foreach (var pair in setCookies.Select(value => value.Split(';', 2)[0].Split('=', 2)))
        {
            jar[pair[0]] = pair.Length > 1 ? pair[1] : string.Empty;
        }
    }

    private static void SetCookieHeader(HttpClient client, Dictionary<string, string> jar)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add(
            "Cookie", string.Join("; ", jar.Select(pair => $"{pair.Key}={pair.Value}")));
    }
}
