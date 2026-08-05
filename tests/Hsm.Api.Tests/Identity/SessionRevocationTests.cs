using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Identity;

public sealed class SessionRevocationFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_session_revocation";
}

/// <summary>
/// A session cookie carries the caller's roles, so a role change has to reach
/// sessions already issued — or a demoted admin stays an admin for as long as
/// their cookie lives.
///
/// <para>The frozen system's ceiling was the 15-minute access token. The
/// replacement is the SECURITY STAMP: <c>ChangeUserRoleHandler</c> bumps it,
/// and the session cookie's <c>OnValidatePrincipal</c> compares it on every
/// request (<c>SecurityStampValidatorOptions.ValidationInterval</c> is zero —
/// see <c>AddHsmIdentityAuthentication</c>). These tests pin both halves:
/// bumping it ends the session, and the role that comes back afterwards is the
/// row's.</para>
/// </summary>
public class SessionRevocationTests(SessionRevocationFactory factory)
    : IClassFixture<SessionRevocationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Demoting_an_admin_ends_their_live_session_on_the_next_request()
    {
        using var victimClient = await factory.AuthenticatedClientAsync(Roles.Admin);
        var victimId = await OwnIdAsync(victimClient);

        // The session works before the change — so the refusal below is the
        // demotion's doing and not a broken client.
        var before = await victimClient.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var otherAdmin = await factory.AuthenticatedClientAsync(Roles.Admin);
        var demote = await otherAdmin.PatchAsJsonAsync(
            $"/api/v1/users/{victimId}", new { role = Roles.Nurse }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);

        var after = await victimClient.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        // 401: the stamp no longer matches, so the cookie is rejected outright
        // and the holder must authenticate again. Not 403 — this is revocation
        // of the SESSION, not a role decision taken on a still-valid one.
        await ProblemAssert.ProblemAsync(after, 401);
    }

    [Fact]
    public async Task The_role_after_re_authenticating_is_the_rows_not_the_old_cookies()
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        using var nurseClient = await factory.AuthenticatedClientAsync(Roles.Nurse, username: username);
        var subjectId = await OwnIdAsync(nurseClient);
        await ProblemAssert.ProblemAsync(
            await nurseClient.GetAsync("/api/v1/users", CancellationToken.None), 403);

        using var admin = await factory.AuthenticatedClientAsync(Roles.Admin);
        var promote = await admin.PatchAsJsonAsync(
            $"/api/v1/users/{subjectId}", new { role = Roles.Admin }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

        // The promotion revoked the old session too, so the caller signs in
        // again — and the cookie they get carries the NEW role.
        using var promoted = await SignedInAsync(username);
        var allowed = await promoted.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task An_unrelated_users_role_change_leaves_other_sessions_alone()
    {
        // The stamp is per account. A blast radius wider than one user would
        // make every role edit an outage.
        using var bystander = await factory.AuthenticatedClientAsync(Roles.Admin);
        using var target = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var targetId = await OwnIdAsync(target);

        var change = await bystander.PatchAsJsonAsync(
            $"/api/v1/users/{targetId}", new { role = Roles.Doctor }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        var stillFine = await bystander.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, stillFine.StatusCode);
    }

    /// <summary>The id behind a session, read through its own profile update.</summary>
    private static async Task<Guid> OwnIdAsync(HttpClient client)
    {
        // UpdateOwnProfileCommand takes no id: the row it returns is the
        // actor's, whoever that is.
        var response = await client.PatchAsJsonAsync(
            "/api/v1/users/me", new { }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<HttpClient> SignedInAsync(string username)
    {
        var client = factory.CreateApiClient();
        var login = await client.PostAsJsonAsync(
            "/api/v1/identity/login",
            new { username, password = SessionRevocationFactory.SeedPassword },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            login.Headers.GetValues("Set-Cookie")
                .First(c => c.StartsWith("hsm.session=", StringComparison.Ordinal))
                .Split(';', 2)[0]);
        return client;
    }
}
