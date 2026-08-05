using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Api.Tests.Identity;

public sealed class IntegrationTokenFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_integration_tokens";
}

/// <summary>
/// Integration accounts are the one caller that cannot hold a browser cookie,
/// so they keep a JWT access token. Their REFRESH token is not a JWT: it is 256
/// bits of opaque entropy, SHA-256'd at rest and rotated on every use, and the
/// tests below pin all three of those properties rather than trusting the code
/// path that produces them.
///
/// <para>Everything here goes over HTTP. Task 13 had to provision in process
/// because the register route did not exist yet; it does now, and dispatching a
/// handler directly would have skipped the pipeline authorization that is half
/// of what these routes are.</para>
/// </summary>
public class IntegrationTokenTests(IntegrationTokenFactory factory)
    : IClassFixture<IntegrationTokenFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ----- register --------------------------------------------------------

    [Fact]
    public async Task An_admin_registering_an_integration_receives_both_tokens()
    {
        using var admin = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await RegisterAsync(admin);

        Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.False(string.IsNullOrEmpty(response.RefreshToken));

        // The access token's lifetime is stated on the wire rather than left for
        // a client to infer by decoding a credential it has no business parsing.
        Assert.Equal((int)TimeSpan.FromDays(1).TotalSeconds, response.ExpiresInSeconds);
    }

    [Fact]
    public async Task A_non_admin_cannot_register_an_integration()
    {
        // A permanent machine credential is the most valuable thing this module
        // hands out. 403, from the request type's own [RequireRole(admin)].
        using var doctor = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await RegisterAsync(doctor);

        await ProblemAssert.ProblemAsync(response.Response, 403);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_register_an_integration()
    {
        using var client = factory.CreateApiClient();

        var response = await RegisterAsync(client);

        await ProblemAssert.ProblemAsync(response.Response, 401);
    }

    [Fact]
    public async Task The_access_token_authenticates_a_bearer_call_as_the_integration()
    {
        var issued = await ProvisionAsync();
        using var machine = BearerClient(issued.AccessToken);

        // An authenticated, non-role-gated read: 200 proves the token carried an
        // id, a role and the integration's onboarding exemption through the
        // pipeline.
        var reached = await machine.GetAsync("/api/v1/templates", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, reached.StatusCode);

        // And the role was WEIGHED rather than the token merely accepted: the
        // admin collection refuses this caller.
        var refused = await machine.GetAsync("/api/v1/users", CancellationToken.None);
        await ProblemAssert.ProblemAsync(refused, 403);
    }

    [Fact]
    public async Task An_integration_has_no_user_profile_to_read()
    {
        // GET /api/v1/identity/me reads the USER row, and an integration's
        // subject is an integration row. 404 rather than a synthesised profile.
        var issued = await ProvisionAsync();
        using var machine = BearerClient(issued.AccessToken);

        var response = await machine.GetAsync("/api/v1/identity/me", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    // ----- the refresh token itself ---------------------------------------

    [Fact]
    public async Task The_refresh_token_is_opaque_entropy_and_not_a_token_anyone_can_read()
    {
        var first = await ProvisionAsync();
        var second = await ProvisionAsync();

        // Not a JWT: no dot-separated segments, and none of the base64 "eyJ"
        // that every JSON header starts with. There is nothing in here to
        // decode, which is the property — a refresh token carries no claim a
        // reader could act on and nothing a leak could disclose.
        Assert.DoesNotContain(".", first.RefreshToken, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", first.RefreshToken, StringComparison.Ordinal);

        // 256 bits, URL-safe, and actually decodable as such — a token that
        // silently shrank to 8 bytes would still look opaque.
        Assert.Equal(32, Base64Url.DecodeFromChars(first.RefreshToken).Length);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
    }

    [Fact]
    public async Task The_refresh_token_is_never_stored_in_the_clear()
    {
        var issued = await ProvisionAsync();

        var stored = await factory.WithDbAsync(db =>
            db.IntegrationRefreshTokens.Select(t => t.TokenHash).ToListAsync(CancellationToken.None));

        // The row holds a digest. A database read — a backup, a replica, a
        // support query — cannot reproduce the credential.
        Assert.DoesNotContain(issued.RefreshToken, stored, StringComparer.Ordinal);
        Assert.All(stored, hash => Assert.Equal(64, hash.Length));
    }

    // ----- refresh ---------------------------------------------------------

    [Fact]
    public async Task Refreshing_returns_a_different_refresh_token_and_retires_the_old_one()
    {
        var issued = await ProvisionAsync();
        using var client = factory.CreateApiClient();

        var refreshed = await RefreshAsync(client, issued.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, refreshed.Response.StatusCode);
        Assert.False(string.IsNullOrEmpty(refreshed.AccessToken));
        Assert.NotEqual(issued.RefreshToken, refreshed.RefreshToken);

        // Rotation means the OLD token stops working — otherwise a leaked
        // refresh token stays valid forever no matter what.
        var replayed = await RefreshAsync(client, issued.RefreshToken);
        await ProblemAssert.ProblemAsync(replayed.Response, 401);

        // ...and the new one does work, so rotation replaced the credential
        // rather than merely destroying it.
        var again = await RefreshAsync(client, refreshed.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, again.Response.StatusCode);
    }

    [Fact]
    public async Task A_rotated_access_token_still_authenticates_the_integration()
    {
        var issued = await ProvisionAsync();
        using var client = factory.CreateApiClient();
        var refreshed = await RefreshAsync(client, issued.RefreshToken);

        using var machine = BearerClient(refreshed.AccessToken);
        var response = await machine.GetAsync("/api/v1/templates", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_garbage_refresh_token_is_401()
    {
        using var client = factory.CreateApiClient();

        var response = await RefreshAsync(client, "not-a-refresh-token");

        await ProblemAssert.ProblemAsync(response.Response, 401);
    }

    [Fact]
    public async Task An_access_token_cannot_be_used_as_a_refresh_token()
    {
        // They are different KINDS of thing now, not two JWTs with different
        // secrets: the refresh route hashes what it is given and looks for a
        // row. A signed access token matches nothing.
        var issued = await ProvisionAsync();
        using var client = factory.CreateApiClient();

        var response = await RefreshAsync(client, issued.AccessToken);

        await ProblemAssert.ProblemAsync(response.Response, 401);
    }

    [Fact]
    public async Task A_browser_cookie_session_cannot_refresh()
    {
        // The route serves integrations and NOTHING else. A signed-in browser
        // has no refresh token to present — it holds a sliding session cookie —
        // and the cookie itself buys nothing here: presenting one alongside an
        // unredeemable value is still a 401, not a rotation.
        using var browser = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await RefreshAsync(browser, string.Empty);

        await ProblemAssert.ProblemAsync(response.Response, 401);
    }

    [Fact]
    public async Task Two_concurrent_refreshes_with_the_same_token_produce_exactly_one_winner()
    {
        // The atomicity claim, tested rather than asserted in a comment. Both
        // requests find the same active row; rotation happens inside the
        // command's transaction, so the row can be claimed once. If the claim
        // were a plain read-then-write, both would rotate and the account would
        // end up with two live refresh tokens from one.
        var issued = await ProvisionAsync();
        using var first = factory.CreateApiClient();
        using var second = factory.CreateApiClient();

        var outcomes = await Task.WhenAll(
            RefreshAsync(first, issued.RefreshToken),
            RefreshAsync(second, issued.RefreshToken));

        Assert.Single(outcomes, o => o.Response.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(outcomes, o => o.Response.StatusCode != HttpStatusCode.OK);
        await ProblemAssert.ProblemAsync(loser.Response, 401);

        // Exactly one active row survives — the winner's.
        var winner = outcomes.Single(o => o.Response.StatusCode == HttpStatusCode.OK);
        using var client = factory.CreateApiClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await RefreshAsync(client, winner.RefreshToken)).Response.StatusCode);
    }

    // ----- logout ----------------------------------------------------------

    [Fact]
    public async Task An_admin_signs_an_integration_out_by_presenting_its_token()
    {
        var issued = await ProvisionAsync();
        using var admin = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(
            "/api/v1/identity/integrations/logout",
            new { token = issued.RefreshToken },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The credential is dead: the refresh token it revoked cannot rotate.
        using var client = factory.CreateApiClient();
        await ProblemAssert.ProblemAsync(
            (await RefreshAsync(client, issued.RefreshToken)).Response, 401);
    }

    [Fact]
    public async Task An_admin_signs_an_integration_out_by_presenting_its_access_token()
    {
        // Either half of the pair identifies the account. The access token names
        // it in a claim; the refresh token matches a row.
        var issued = await ProvisionAsync();
        using var admin = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(
            "/api/v1/identity/integrations/logout",
            new { token = issued.AccessToken },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_non_admin_cannot_sign_an_integration_out()
    {
        var issued = await ProvisionAsync();
        using var doctor = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await doctor.PostAsJsonAsync(
            "/api/v1/identity/integrations/logout",
            new { token = issued.RefreshToken },
            CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }

    // ----- plumbing --------------------------------------------------------

    /// <summary>Registers an integration through the route, as an admin.</summary>
    private async Task<IssuedTokens> ProvisionAsync()
    {
        using var admin = await factory.AuthenticatedClientAsync(Roles.Admin);
        var issued = await RegisterAsync(admin);
        Assert.Equal(HttpStatusCode.Created, issued.Response.StatusCode);
        return issued;
    }

    private static async Task<IssuedTokens> RegisterAsync(HttpClient client) =>
        await IssuedTokens.FromAsync(await client.PostAsJsonAsync(
            "/api/v1/identity/integrations/register",
            new
            {
                name = $"machine_{Guid.NewGuid():N}",
                description = "integration token tests",
                functionality = "dev",
            },
            CancellationToken.None));

    private static async Task<IssuedTokens> RefreshAsync(HttpClient client, string refreshToken) =>
        await IssuedTokens.FromAsync(await client.PostAsJsonAsync(
            "/api/v1/identity/refresh",
            new { refreshToken },
            CancellationToken.None));

    private HttpClient BearerClient(string accessToken)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        return client;
    }

    private sealed record IssuedTokens(
        HttpResponseMessage Response, string AccessToken, string RefreshToken, int ExpiresInSeconds)
    {
        public static async Task<IssuedTokens> FromAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new IssuedTokens(response, string.Empty, string.Empty, 0);
            }

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
            return new IssuedTokens(
                response,
                body.GetProperty("accessToken").GetString()!,
                body.GetProperty("refreshToken").GetString()!,
                body.GetProperty("expiresInSeconds").GetInt32());
        }
    }
}
