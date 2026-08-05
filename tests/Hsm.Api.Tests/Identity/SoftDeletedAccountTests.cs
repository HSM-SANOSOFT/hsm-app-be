using System.Net;
using System.Net.Http.Json;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Api.Tests.Identity;

public sealed class SoftDeletedAccountFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_soft_deleted";
}

/// <summary>
/// Sign-in must resolve the LIVE row, not merely "a row with this name".
///
/// <para>The unique indexes on <c>users</c> are filtered on DeletedAt precisely
/// so a soft-deleted account frees its username and email for reuse — which
/// means a soft-deleted row and a live row can legitimately share both, and
/// <c>UserManager.FindByNameAsync</c>, an unfiltered FirstOrDefault on the
/// normalized column, may return either. The deleted <c>UserStore</c> carried
/// <c>DeletedAt == null</c> inside the query; <c>IUserDirectory.FindLiveByNameAsync</c>
/// is what carries it now, and this suite is what stops it being dropped again.
/// Nothing in <c>src/</c> writes DeletedAt yet, so the rows are made here the
/// way the feature eventually will.</para>
/// </summary>
public class SoftDeletedAccountTests(SoftDeletedAccountFactory factory)
    : IClassFixture<SoftDeletedAccountFactory>, IAsyncLifetime
{
    private const string ReplacementPassword = "Replacement-Passw0rd";

    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_soft_deleted_account_cannot_sign_in()
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        await factory.SeedUserAsync(
            username, SoftDeletedAccountFactory.SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);
        using var client = factory.CreateApiClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await SignInAsync(client, username, SoftDeletedAccountFactory.SeedPassword)).StatusCode);

        await SoftDeleteAsync(username);

        var response = await SignInAsync(client, username, SoftDeletedAccountFactory.SeedPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_reused_username_signs_in_as_the_live_account_not_the_deleted_one()
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        await factory.SeedUserAsync(
            username, SoftDeletedAccountFactory.SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);
        await SoftDeleteAsync(username);
        await ReprovisionAsync(username);
        using var client = factory.CreateApiClient();

        // The replacement's password works...
        var live = await SignInAsync(client, username, ReplacementPassword);
        // ...and the soft-deleted row's password does not, which is what proves
        // the lookup resolved the live row rather than happening to find it.
        var deleted = await SignInAsync(client, username, SoftDeletedAccountFactory.SeedPassword);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deleted.StatusCode);
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

    /// <summary>
    /// Writes the replacement row through the DbContext, not UserManager:
    /// Identity's UserValidator enforces uniqueness through the same unfiltered
    /// lookup this suite is about, so CreateAsync still sees the soft-deleted
    /// row and refuses. That is a separate consumer of the same unfiltered
    /// lookup, reported as a follow-up — provisioning, not sign-in, which is
    /// what is under test here. The database allows the pair; the indexes are
    /// filtered on DeletedAt.
    /// </summary>
    private async Task ReprovisionAsync(string username)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.HsmDbContext>();
        var replacement = new HsmUser
        {
            Id = Guid.NewGuid(),
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            Email = $"{Guid.NewGuid():N}@api.test",
            NormalizedEmail = $"{Guid.NewGuid():N}@API.TEST".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            FirstName = "Live",
            FirstLastName = "Again",
            OnboardingCompletedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        replacement.PasswordHash = users.PasswordHasher.HashPassword(replacement, ReplacementPassword);
        db.Users.Add(replacement);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static Task<HttpResponseMessage> SignInAsync(
        HttpClient client, string username, string password) =>
        client.PostAsJsonAsync(
            "/api/v1/identity/login", new { username, password }, CancellationToken.None);
}
