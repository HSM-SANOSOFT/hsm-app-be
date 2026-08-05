using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Identity;

public sealed class IdentityEndpointFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_identity";
}

/// <summary>
/// The identity resource end to end: register, login, logout, me, onboarding
/// and the three account-recovery routes, at their new
/// <c>/api/v1/identity</c> paths and with real status codes.
///
/// <para>Enumeration-safety is asserted here rather than left to the handlers'
/// comments, because it is the one property of this module that a reader
/// cannot see by looking at a single response: it only exists in the
/// DIFFERENCE between the known-account and unknown-account answers, and the
/// tests below compare them byte for byte.</para>
/// </summary>
public class IdentityEndpointTests(IdentityEndpointFactory factory)
    : IClassFixture<IdentityEndpointFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private static object RegisterBody(string username) => new
    {
        username,
        email = $"{username}@register.test",
        password = "Register-Passw0rd",
        firstName = "Nueva",
        firstLastName = "Paciente",
    };

    /// <summary>The <c>name=value</c> pairs a response's Set-Cookie headers carry.</summary>
    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private static string SessionCookieOf(HttpResponseMessage response) =>
        Assert.Single(SetCookies(response), c => c.StartsWith("hsm.session=", StringComparison.Ordinal))
            .Split(';', 2)[0];

    // ----- register / login / logout / me ---------------------------------

    [Fact]
    public async Task Register_returns_201_with_the_new_patient_and_a_session()
    {
        using var client = factory.CreateApiClient();
        var username = Unique("p");

        var response = await client.PostAsJsonAsync(
            "/api/v1/identity/register", RegisterBody(username), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(username, me.GetProperty("username").GetString());
        Assert.Equal($"{username}@register.test", me.GetProperty("email").GetString());
        Assert.Contains(Roles.Patient, me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        // Patients never do the staff first-login flow.
        Assert.NotEqual(JsonValueKind.Null, me.GetProperty("onboardingCompletedAt").ValueKind);
        // No token pair on the wire any more, and no password material either.
        Assert.False(me.TryGetProperty("access_token", out _));
        Assert.False(me.TryGetProperty("passwordHash", out _));

        // The session the response opened actually works.
        var session = SessionCookieOf(response);
        using var signedIn = factory.CreateApiClient();
        signedIn.DefaultRequestHeaders.Add("Cookie", session);
        var profile = await signedIn.GetAsync("/api/v1/identity/me", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Fact]
    public async Task Login_returns_200_with_the_user_and_a_session()
    {
        var username = Unique("u");
        await factory.SeedUserAsync(
            username, IdentityEndpointFactory.SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/identity/login",
            new { username, password = IdentityEndpointFactory.SeedPassword },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(username, me.GetProperty("username").GetString());
        Assert.Contains(Roles.Doctor, me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.NotEmpty(SessionCookieOf(response));
    }

    [Fact]
    public async Task Login_answers_one_identical_401_for_every_way_it_can_fail()
    {
        // An unknown username and a wrong password must be INDISTINGUISHABLE,
        // or the sign-in form answers "does this account exist" one refusal at
        // a time. (LockoutTests pins the third way — a locked account — against
        // the same bar.)
        var seeded = Unique("u");
        await factory.SeedUserAsync(
            seeded, IdentityEndpointFactory.SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);
        using var client = factory.CreateApiClient();

        var unknownAccount = await client.PostAsJsonAsync(
            "/api/v1/identity/login",
            new { username = "no-such-account", password = "whatever-Passw0rd" },
            CancellationToken.None);
        var wrongPassword = await client.PostAsJsonAsync(
            "/api/v1/identity/login",
            new { username = seeded, password = "the-wrong-Passw0rd" },
            CancellationToken.None);

        await ProblemAssert.ProblemAsync(unknownAccount, 401);
        await ProblemAssert.ProblemAsync(wrongPassword, 401);
        Assert.Equal(
            await WithoutTraceIdAsync(unknownAccount), await WithoutTraceIdAsync(wrongPassword));
    }

    /// <summary>The response body with the one member that legitimately differs removed.</summary>
    private static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None))!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }

    [Fact]
    public async Task Logout_returns_204_and_expires_the_session_cookie()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.PostAsync(
            new Uri("/api/v1/identity/logout", UriKind.Relative), content: null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // Sign-out is the cookie being taken back, so the proof is the
        // Set-Cookie that empties it rather than a later request failing: this
        // client replays a fixed header and would keep sending the old value.
        var cleared = Assert.Single(
            SetCookies(response), c => c.StartsWith("hsm.session=;", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);

        // And a caller that honours it — i.e. any browser — is anonymous again.
        client.DefaultRequestHeaders.Remove("Cookie");
        var after = await client.GetAsync("/api/v1/identity/me", CancellationToken.None);
        await ProblemAssert.ProblemAsync(after, 401);
    }

    [Fact]
    public async Task Me_returns_the_calling_users_own_row()
    {
        var username = Unique("u");
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse, username: username);

        var response = await client.GetAsync("/api/v1/identity/me", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(username, me.GetProperty("username").GetString());
        Assert.Contains(Roles.Nurse, me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.False(me.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task Me_is_reachable_by_a_user_whose_onboarding_is_still_pending()
    {
        // The [AllowPendingOnboarding] proof: every other authenticated route
        // refuses this actor with a 403.
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse, onboarded: false);

        var response = await client.GetAsync("/api/v1/identity/me", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(JsonValueKind.Null, me.GetProperty("onboardingCompletedAt").ValueKind);

        var blocked = await client.GetAsync("/api/v1/templates", CancellationToken.None);
        await ProblemAssert.ProblemAsync(blocked, 403);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_read_me()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/identity/me", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    // ----- onboarding ------------------------------------------------------

    [Fact]
    public async Task Onboarding_completes_the_account_and_keeps_the_callers_session()
    {
        var username = Unique("s");
        using var client = await factory.AuthenticatedClientAsync(
            Roles.Doctor, onboarded: false, username: username);

        var response = await client.PostAsJsonAsync(
            "/api/v1/identity/onboarding",
            new
            {
                newPassword = "Onboarded-Passw0rd",
                phoneNumber = "+34600111222",
                confirmEmail = $"{username}@api.test",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.NotEqual(JsonValueKind.Null, me.GetProperty("onboardingCompletedAt").ValueKind);
        Assert.Equal("+34600111222", me.GetProperty("phoneNumber").GetString());
        // No token pair: completing onboarding hands back the user, and the
        // door hands back a session.
        Assert.False(me.TryGetProperty("refresh_token", out _));

        // Setting the password rotated the security stamp, which kills every
        // cookie for the account — including this one, unless the route
        // reissued it. Replaying the REISSUED cookie is what proves it did.
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", SessionCookieOf(response));
        var after = await client.GetAsync("/api/v1/identity/me", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var reread = await after.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.NotEqual(JsonValueKind.Null, reread.GetProperty("onboardingCompletedAt").ValueKind);
    }

    [Fact]
    public async Task Onboarding_refuses_a_confirmation_email_that_is_not_the_accounts()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor, onboarded: false);

        var response = await client.PostAsJsonAsync(
            "/api/v1/identity/onboarding",
            new
            {
                newPassword = "Onboarded-Passw0rd",
                phoneNumber = "+34600111222",
                confirmEmail = "someone.else@api.test",
            },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("confirmEmail", out _));
    }

    // ----- recovery, and the enumeration-safety it exists for --------------

    [Fact]
    public async Task Forgot_password_answers_202_identically_for_a_known_and_an_unknown_email()
    {
        var username = Unique("f");
        await factory.SeedUserAsync(
            username, IdentityEndpointFactory.SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);
        using var client = factory.CreateApiClient();

        var known = await client.PostAsJsonAsync(
            "/api/v1/identity/password/forgot",
            new { email = $"{username}@api.test" },
            CancellationToken.None);
        var unknown = await client.PostAsJsonAsync(
            "/api/v1/identity/password/forgot",
            new { email = $"{Guid.NewGuid():N}@nobody.test" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(
            await known.Content.ReadAsStringAsync(CancellationToken.None),
            await unknown.Content.ReadAsStringAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Recover_username_answers_202_identically_for_a_known_and_an_unknown_email()
    {
        var username = Unique("r");
        await factory.SeedUserAsync(
            username, IdentityEndpointFactory.SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);
        using var client = factory.CreateApiClient();

        var known = await client.PostAsJsonAsync(
            "/api/v1/identity/username/recover",
            new { email = $"{username}@api.test" },
            CancellationToken.None);
        var unknown = await client.PostAsJsonAsync(
            "/api/v1/identity/username/recover",
            new { email = $"{Guid.NewGuid():N}@nobody.test" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(
            await known.Content.ReadAsStringAsync(CancellationToken.None),
            await unknown.Content.ReadAsStringAsync(CancellationToken.None));
        // And the username itself never rides the response — it goes by email.
        Assert.DoesNotContain(
            username,
            await known.Content.ReadAsStringAsync(CancellationToken.None),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-link")]
    [InlineData("00000000000000000000000000000000.made-up-token")]
    public async Task Reset_password_answers_one_identical_400_on_token_for_every_bad_token(string token)
    {
        // A malformed link and a well-formed link naming an account that does
        // not exist must produce the same failure — the reset form must not
        // answer "does this account exist" either.
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/identity/password/reset",
            new { token, newPassword = "Brand-New-Passw0rd" },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.Equal(
            "The password reset link is invalid or has expired.",
            problem.GetProperty("errors").GetProperty("token")[0].GetString());
    }

    // ----- the retired surface --------------------------------------------

    [Theory]
    [InlineData("POST", "/v1/auth/signup")]
    [InlineData("POST", "/v1/auth/login")]
    [InlineData("GET", "/v1/auth/logout")]
    [InlineData("GET", "/v1/auth/profile")]
    [InlineData("GET", "/v1/auth/csrf")]
    [InlineData("POST", "/v1/auth/onboarding")]
    [InlineData("POST", "/v1/auth/password/forgot")]
    [InlineData("POST", "/v1/auth/password/reset")]
    [InlineData("POST", "/v1/auth/username/recover")]
    [InlineData("POST", "/v1/auth/pin/generate")]
    [InlineData("POST", "/v1/auth/pin/validate")]
    [InlineData("POST", "/v1/auth/signup/integration")]
    [InlineData("POST", "/v1/auth/logout/integration")]
    [InlineData("POST", "/api/v1/auth/login")]
    [InlineData("GET", "/api/v1/auth/profile")]
    public async Task The_retired_auth_surface_is_unrouted(string method, string route)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { }),
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_integration_refresh_route_is_deliberately_still_routed()
    {
        // The ONE survivor of /v1/auth, and the only reason this task did not
        // delete AuthEndpoints outright: an integration's refresh token has no
        // other redemption path until Task 14 introduces
        // POST /api/v1/identity/refresh. 401 (no credential presented), not
        // 404 — the route is there.
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/v1/auth/refresh", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }
}

/// <summary>
/// Its own host, and therefore its own rate-limiter partitions: the per-IP
/// window is 10 requests per route per minute, so exercising it in the suite
/// above would spend a budget the enumeration tests share.
/// </summary>
public sealed class IdentityRateLimitFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_identity_ratelimit";
}

public class IdentityRateLimitTests(IdentityRateLimitFactory factory)
    : IClassFixture<IdentityRateLimitFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("/api/v1/identity/password/forgot")]
    [InlineData("/api/v1/identity/password/reset")]
    [InlineData("/api/v1/identity/username/recover")]
    public async Task The_recovery_routes_refuse_the_eleventh_call_in_the_window(string route)
    {
        // The per-IP edge limit, partitioned by "{ip}:{path}" — the second of
        // the module's two, alongside the per-account limit inside
        // ForgotPasswordHandler. Unknown emails throughout: this is the limit
        // that must hold for a caller probing accounts it has not found yet.
        using var client = factory.CreateApiClient();
        var body = new
        {
            email = $"{Guid.NewGuid():N}@nobody.test",
            token = "not-a-link",
            newPassword = "Brand-New-Passw0rd",
        };

        for (var call = 0; call < 10; call++)
        {
            var allowed = await client.PostAsJsonAsync(route, body, CancellationToken.None);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        var refused = await client.PostAsJsonAsync(route, body, CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
    }
}
