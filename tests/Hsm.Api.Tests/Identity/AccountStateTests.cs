using System.Net;
using System.Net.Http.Json;
using Hsm.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Api.Tests.Identity;

public sealed class AccountStateFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_account_state";
}

/// <summary>
/// <c>HsmUser.IsActive</c> and <c>HsmUser.DeletedAt</c> are on the wire
/// (<c>UserResource</c>) and on the admin screen — this is the suite that says
/// they MEAN something on the door humans use, at both moments they could:
/// when a credential is asked for, and while one is already held.
///
/// <para>The second half is the one that was missing. Refusing a fresh sign-in
/// is easy and was already true for soft delete; leaving an existing session
/// alive is the actual exposure, because a session slides for eight hours.
/// Neither half is enforced by Identity for free: <c>SignInManager</c> and the
/// security-stamp validator both resolve the account through the UNFILTERED
/// <c>UserManager</c> lookups, which have no query filter on <c>DeletedAt</c>
/// and no opinion at all about <c>IsActive</c>. <c>LoginHandler</c> and
/// <c>HsmSessionValidator</c> are what close each half, and this is what stops
/// either being dropped again.</para>
///
/// <para>Nothing in <c>src/</c> writes either field yet, so the rows are
/// changed here the way the admin feature eventually will — the same approach
/// <c>SoftDeletedAccountTests</c> takes.</para>
/// </summary>
public class AccountStateTests(AccountStateFactory factory)
    : IClassFixture<AccountStateFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_deactivated_account_cannot_sign_in()
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        await factory.SeedUserAsync(
            username, AccountStateFactory.SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);
        using var client = factory.CreateApiClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await SignInAsync(client, username)).StatusCode);

        await DeactivateAsync(username);

        var response = await SignInAsync(client, username);

        // The SAME 401 an unknown username, a wrong password and a locked-out
        // account get. A distinguishable "this account is switched off" would
        // hand an attacker a directory of real usernames.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deactivating_an_account_ends_the_session_it_already_holds()
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin, username: username);

        // Works before the change, so the refusal below is the deactivation's
        // doing and not a broken client.
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None)).StatusCode);

        await DeactivateAsync(username);

        var after = await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        // On the NEXT request, not eventually: the cookie is revalidated every
        // request, so the window is one request wide.
        await ProblemAssert.ProblemAsync(after, 401);
    }

    [Fact]
    public async Task Soft_deleting_an_account_ends_the_session_it_already_holds()
    {
        // The gap the security stamp does not cover: nothing bumps the stamp
        // when a row is soft-deleted, and UserManager.FindByIdAsync has no
        // query filter on DeletedAt — so on Identity's own machinery alone this
        // session would keep validating for the rest of its sliding window.
        var username = $"u{Guid.NewGuid():N}"[..20];
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin, username: username);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None)).StatusCode);

        await SoftDeleteAsync(username);

        var after = await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        await ProblemAssert.ProblemAsync(after, 401);
    }

    [Fact]
    public async Task Deactivating_one_account_leaves_another_ones_session_alone()
    {
        // The blast radius is one account. A check that ended everybody's
        // session would be an outage, not a control.
        var username = $"u{Guid.NewGuid():N}"[..20];
        using var target = await factory.AuthenticatedClientAsync(Roles.Admin, username: username);
        using var bystander = await factory.AuthenticatedClientAsync(Roles.Admin);

        await DeactivateAsync(username);

        await ProblemAssert.ProblemAsync(
            await target.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None), 401);
        Assert.Equal(
            HttpStatusCode.OK,
            (await bystander.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None)).StatusCode);
    }

    private Task<int> DeactivateAsync(string username)
    {
        var normalized = username.ToUpperInvariant();
        return factory.WithDbAsync(db =>
            db.Users
                .Where(u => u.NormalizedUserName == normalized && u.DeletedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(u => u.IsActive, false), CancellationToken.None));
    }

    private Task<int> SoftDeleteAsync(string username)
    {
        var normalized = username.ToUpperInvariant();
        return factory.WithDbAsync(db =>
            db.Users
                .Where(u => u.NormalizedUserName == normalized && u.DeletedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(u => u.DeletedAt, DateTimeOffset.UtcNow),
                    CancellationToken.None));
    }

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string username) =>
        client.PostAsJsonAsync(
            "/api/v1/identity/login",
            new { username, password = AccountStateFactory.SeedPassword },
            CancellationToken.None);
}
