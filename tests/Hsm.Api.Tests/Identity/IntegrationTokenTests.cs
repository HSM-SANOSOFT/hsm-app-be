using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Hsm.Api.Tests.Identity;

public sealed class IntegrationTokenFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_integration_tokens";

    /// <summary>
    /// This suite exercises what refresh DOES, not how often it may be called,
    /// and it makes ~25 refresh calls from one address inside one 60-second
    /// window. Left at the production budget it would sit a few calls from a
    /// 429 that has nothing to do with anything under test, and the next
    /// refresh test anyone adds would fail for a reason its author could not
    /// guess. The throttle itself is pinned by <c>IdentityRateLimitTests</c>,
    /// on its own host, at the real number.
    /// </summary>
    protected override void ConfigureModule(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting("Auth:RefreshRequestsPerMinute", "10000");

        // The reuse alarm's ONLY output is a log line, so the log has to be
        // readable for the alarm to be testable at all.
        builder.ConfigureServices(services => services.AddFakeLogging());
    }

    /// <summary>Every log record this host has written.</summary>
    public IReadOnlyList<FakeLogRecord> LogRecords =>
        Services.GetRequiredService<FakeLogCollector>().GetSnapshot();
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

        // The new token works, so rotation REPLACED the credential rather than
        // merely destroying it. Asserted before the replay below, not after:
        // replaying a spent token is reuse detection's trigger and revokes the
        // whole chain, so afterwards nothing works by design.
        var again = await RefreshAsync(client, refreshed.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, again.Response.StatusCode);

        // Rotation means the OLD token stops working — otherwise a leaked
        // refresh token stays valid forever no matter what.
        var replayed = await RefreshAsync(client, issued.RefreshToken);
        await ProblemAssert.ProblemAsync(replayed.Response, 401);
    }

    [Fact]
    public async Task Replaying_a_spent_refresh_token_revokes_the_whole_chain()
    {
        // REUSE DETECTION. A spent digest coming back means two parties hold
        // copies of a credential that has since moved on — the legitimate
        // integration and whoever took it. There is no way to tell which one is
        // asking, so the only safe answer is to trust neither: kill what is
        // currently live and make the account's own next refresh fail too.
        //
        // Without this, the first party to redeem a stolen token simply BECOMES
        // the account. The legitimate holder's next refresh 401s exactly like a
        // normal failure, nothing is alerted, and the takeover is permanent and
        // silent. Failing loudly for both parties is the correct outcome: it
        // forces a human to re-provision.
        var issued = await ProvisionAsync();
        using var client = factory.CreateApiClient();

        // TWICE, so the replayed digest is not the account's most recently spent
        // one. That is the "this token is genuinely stale" shape — a digest two
        // rotations back cannot be a client colliding with itself.
        var second = await RefreshAsync(client, issued.RefreshToken);
        var third = await RefreshAsync(client, second.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, third.Response.StatusCode);

        // The stale token comes back — the alarm.
        var before = factory.LogRecords.Count;
        var replayed = await RefreshAsync(client, issued.RefreshToken);
        await ProblemAssert.ProblemAsync(replayed.Response, 401);

        // ...and the chain is dead. This is the assertion that distinguishes
        // reuse DETECTION from merely refusing a spent token: the live
        // credential minted a moment ago no longer works either.
        var afterAlarm = await RefreshAsync(client, third.RefreshToken);
        await ProblemAssert.ProblemAsync(afterAlarm.Response, 401);

        // AND SOMEBODY WAS TOLD. The 401 is identical to an ordinary failure by
        // design, so without a log the only signal a human ever gets is an
        // integration that mysteriously stopped working. The account id is what
        // makes the record actionable; the token must never appear in it.
        var alarm = Assert.Single(
            factory.LogRecords.Skip(before),
            r => r.Level == LogLevel.Warning
                && r.Message.Contains("REUSE detected", StringComparison.Ordinal));
        Assert.Contains(
            issued.AccountIdFromAccessToken, alarm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(issued.RefreshToken, alarm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_recent_replay_of_the_most_recent_digest_is_refused_without_revoking()
    {
        // THE GRACE WINDOW, tested deterministically. This is the same state a
        // client colliding with itself produces — a digest spent moments ago,
        // still the account's most recent — reached by rotating and replaying in
        // sequence rather than by racing, so the assertion does not depend on
        // which request the scheduler happened to run first.
        //
        // The replay is still REFUSED. The window forgives the alarm, not the
        // token: a caller that lands here gets a clean 401 and should refresh
        // again from the credential it holds. It is explicitly NOT handed the
        // successor token, which would make a spent digest redeemable and give
        // a thief exactly the replay they want.
        var issued = await ProvisionAsync();
        using var client = factory.CreateApiClient();
        var rotated = await RefreshAsync(client, issued.RefreshToken);

        await ProblemAssert.ProblemAsync(
            (await RefreshAsync(client, issued.RefreshToken)).Response, 401);

        // The live credential survived, which is the whole point.
        Assert.Equal(
            HttpStatusCode.OK,
            (await RefreshAsync(client, rotated.RefreshToken)).Response.StatusCode);
    }

    [Fact]
    public async Task A_stale_replay_of_the_most_recent_digest_still_revokes_the_chain()
    {
        // The grace window is a window in TIME, not an exemption for the most
        // recent digest. This is the same replay the window forgives, aged past
        // it by backdating the row's UpdatedAt — the one thing a test cannot
        // wait out — so the boundary itself is pinned rather than assumed.
        var issued = await ProvisionAsync();
        using var client = factory.CreateApiClient();
        var rotated = await RefreshAsync(client, issued.RefreshToken);

        await AgeSpentTokenAsync(issued.RefreshToken, TimeSpan.FromMinutes(5));

        await ProblemAssert.ProblemAsync(
            (await RefreshAsync(client, issued.RefreshToken)).Response, 401);

        // Outside the window, a replay is theft again.
        await ProblemAssert.ProblemAsync(
            (await RefreshAsync(client, rotated.RefreshToken)).Response, 401);
    }

    [Fact]
    public async Task A_refresh_body_with_no_token_is_400_and_not_500()
    {
        // {} binds RefreshToken as null. Without a validator that reaches
        // HashRefreshToken(null) and leaves the closed exception set's default
        // branch to render an ArgumentNullException as a 500 — a caller error
        // reported as a server fault.
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/identity/refresh", new { }, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 400);
    }

    [Fact]
    public async Task A_register_body_with_no_fields_is_400_and_not_500()
    {
        using var admin = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(
            "/api/v1/identity/integrations/register", new { }, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 400);
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
        //
        // The value sent is shaped exactly like a real refresh token (32 URL-safe
        // random bytes) rather than empty, so the 401 cannot come from the
        // validator. What is being refused is the CALLER, not the syntax.
        using var browser = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await RefreshAsync(browser, WellFormedButUnissuedToken());

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

        var before = await TokenRowCountAsync();
        var outcomes = await Task.WhenAll(
            RefreshAsync(first, issued.RefreshToken),
            RefreshAsync(second, issued.RefreshToken));

        Assert.Single(outcomes, o => o.Response.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(outcomes, o => o.Response.StatusCode != HttpStatusCode.OK);
        await ProblemAssert.ProblemAsync(loser.Response, 401);

        // EXACTLY ONE ROW WAS WRITTEN, and this is the assertion that catches
        // the bug rather than merely describing the responses. The broken
        // version — deactivating by account instead of by digest — also returns
        // one 200 and one 401 under some interleavings; what it does that this
        // cannot is insert a SECOND live token for the loser. Counting rows
        // across the pair sees that directly.
        //
        Assert.Equal(1, await TokenRowCountAsync() - before);

        // AND THE LOSER DID NOT REVOKE THE WINNER. This is the grace window's
        // whole reason for existing, and it is the dominant real-world trigger:
        // a machine client with two threads and no mutex around its own refresh
        // call, both noticing the access token is near expiry. Without the
        // window the loser sees a digest spent milliseconds ago, reads it as
        // theft, and kills the credential the winner just minted — turning an
        // ordinary scheduling overlap into an outage that needs a human to
        // re-provision.
        using var client = factory.CreateApiClient();
        var winner = outcomes.Single(o => o.Response.StatusCode == HttpStatusCode.OK);
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
    public async Task Signing_out_with_a_superseded_refresh_token_still_kills_what_is_live()
    {
        // The revoke path's version of the same stale-snapshot race. An admin
        // holding a token the account has since rotated past is exactly what
        // losing a race to a concurrent refresh looks like from the admin's
        // side: the token they presented is no longer the live one. Reporting
        // "nothing to revoke" there would tell an operator the credential is
        // dead while a successor is live — the worst possible answer to
        // "revoke this".
        //
        // Deterministic here rather than timing-dependent: rotating first
        // produces the exact state the race produces.
        var issued = await ProvisionAsync();
        using var client = factory.CreateApiClient();
        var rotated = await RefreshAsync(client, issued.RefreshToken);

        using var admin = await factory.AuthenticatedClientAsync(Roles.Admin);
        var response = await admin.PostAsJsonAsync(
            "/api/v1/identity/integrations/logout",
            new { token = issued.RefreshToken },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The successor is dead too, which is what the operator asked for.
        await ProblemAssert.ProblemAsync(
            (await RefreshAsync(client, rotated.RefreshToken)).Response, 401);
    }

    [Fact]
    public async Task Signing_out_an_integration_twice_reports_that_there_was_nothing_left()
    {
        // The 409 is still real, and still means what it says — it just no
        // longer fires for a token that merely lost a race.
        var issued = await ProvisionAsync();
        using var admin = await factory.AuthenticatedClientAsync(Roles.Admin);
        var body = new { token = issued.AccessToken };

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync(
                "/api/v1/identity/integrations/logout", body, CancellationToken.None)).StatusCode);

        var again = await admin.PostAsJsonAsync(
            "/api/v1/identity/integrations/logout", body, CancellationToken.None);

        await ProblemAssert.ProblemAsync(again, 409);
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

    /// <summary>Every refresh-token row in the store, spent ones included.</summary>
    private Task<int> TokenRowCountAsync() =>
        factory.WithDbAsync(db => db.IntegrationRefreshTokens.CountAsync(CancellationToken.None));

    /// <summary>
    /// Backdates a spent row's <c>UpdatedAt</c>, which is the only clock the
    /// grace window reads. Moving the row rather than mocking a clock keeps the
    /// production path — including the comparison under test — completely
    /// unmodified.
    /// </summary>
    private Task<int> AgeSpentTokenAsync(string refreshToken, TimeSpan by)
    {
        var hash = IntegrationTokenIssuer.HashRefreshToken(refreshToken);
        return factory.WithDbAsync(db => db.IntegrationRefreshTokens
            .Where(t => t.TokenHash == hash)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow - by),
                CancellationToken.None));
    }

    /// <summary>
    /// A value indistinguishable in shape from a real refresh token, and issued
    /// to nobody — so a refusal can only be about the row it fails to match.
    /// </summary>
    private static string WellFormedButUnissuedToken() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private HttpClient BearerClient(string accessToken)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        return client;
    }

    private sealed record IssuedTokens(
        HttpResponseMessage Response, string AccessToken, string RefreshToken, int ExpiresInSeconds)
    {
        /// <summary>
        /// The account id, read from the access token's subject claim. The
        /// register response does not carry it — the resource is the credential
        /// and nothing else — and the access token is the one place a caller can
        /// legitimately find it.
        /// </summary>
        public string AccountIdFromAccessToken
        {
            get
            {
                using var payload = JsonDocument.Parse(
                    Base64Url.DecodeFromChars(AccessToken.Split('.')[1]));
                return payload.RootElement.GetProperty("sub").GetString()!;
            }
        }

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
