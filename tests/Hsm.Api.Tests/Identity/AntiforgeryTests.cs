using System.Net;
using System.Net.Http.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Identity;

public sealed class AntiforgeryFactory : ApiFactory
{
    /// <summary>The header the antiforgery options bind the request token to.</summary>
    public const string HsmAntiforgeryHeader = "X-XSRF-TOKEN";

    protected override string DatabaseName => "hsm_api_tests_antiforgery";

    /// <summary>
    /// A seam client with the antiforgery header stripped back off.
    ///
    /// <para>The seam performs the whole handshake — GET
    /// <c>/api/v1/identity/csrf</c>, replay the cookie, echo the token in
    /// <c>X-XSRF-TOKEN</c> — because every module suite mutates through it and
    /// none of them should reimplement that. This suite is the one place that
    /// wants the *unprotected* client, so it undoes the last step rather than
    /// forking the seam.</para>
    /// </summary>
    public async Task<HttpClient> TokenlessClientAsync(string role)
    {
        var client = await AuthenticatedClientAsync(role);
        client.DefaultRequestHeaders.Remove(HsmAntiforgeryHeader);
        return client;
    }
}

public class AntiforgeryTests(AntiforgeryFactory factory)
    : IClassFixture<AntiforgeryFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_cookie_authenticated_unsafe_request_without_the_header_is_403()
    {
        using var client = await factory.TokenlessClientAsync(Roles.Doctor);

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { currentPassword = AntiforgeryFactory.SeedPassword, newPassword = "New-Passw0rd" },
            CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }

    [Fact]
    public async Task The_same_request_with_the_header_succeeds()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        // The header (and its cookie half) came from the seam's own
        // /api/v1/identity/csrf handshake — the only difference from the test
        // above.
        Assert.True(client.DefaultRequestHeaders.Contains(AntiforgeryFactory.HsmAntiforgeryHeader));

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { currentPassword = AntiforgeryFactory.SeedPassword, newPassword = "New-Passw0rd" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Safe_methods_never_need_the_header()
    {
        using var client = await factory.TokenlessClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_bearer_authenticated_unsafe_request_never_needs_the_header()
    {
        // A bearer caller cannot be CSRF'd: nothing is sent ambiently. Requiring
        // a token from integrations would be theatre that breaks them.
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-token");

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { currentPassword = "x", newPassword = "y" },
            CancellationToken.None);

        // 401 from the bearer handler — NOT 403 from antiforgery.
        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task A_bearer_token_beats_a_cookie_on_the_same_request()
    {
        // Precedence, stated as a test: the adaptive scheme forwards to the
        // bearer handler whenever an Authorization header is present, so a
        // still-valid session cookie riding alongside a bad token cannot
        // rescue it. The same client succeeds without the header (the test
        // above), which is what makes this a precedence proof rather than a
        // "bad token is refused" restatement.
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var withCookieOnly = await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, withCookieOnly.StatusCode);

        client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-token");
        var withBearer = await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        await ProblemAssert.ProblemAsync(withBearer, 401);
    }
}
