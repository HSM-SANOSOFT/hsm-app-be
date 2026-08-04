using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Identity;

public sealed class LockoutFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_lockout";
}

/// <summary>
/// Lockout is the one thing Identity's membership brings that the frozen system
/// had no equivalent for, and it is worth its own suite because it is EASY to
/// get silently wrong: the failure counter is written by a handler that then
/// THROWS, and every command runs inside a transaction the pipeline rolls back
/// on exactly that throw. A counter that never survives its own request is a
/// lockout that never engages, and nothing else in the suite would notice —
/// every sign-in test would keep passing.
///
/// <para>The suite also pins the refusals themselves: a locked account, an
/// unknown username and a wrong password all answer identically, because
/// telling a caller "this account is locked" tells them the account exists.</para>
/// </summary>
public class LockoutTests(LockoutFactory factory) : IClassFixture<LockoutFactory>, IAsyncLifetime
{
    private const int MaxAttempts = 10;

    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_configured_number_of_failures_locks_the_account_out()
    {
        var username = await SeedAsync();
        using var client = factory.CreateApiClient();

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var refused = await SignInAsync(client, username, "definitely-not-the-password");
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        // The RIGHT password, and still refused: the failures were persisted
        // rather than rolled back with the failing request.
        var locked = await SignInAsync(client, username, LockoutFactory.SeedPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_the_failures_that_came_before_it()
    {
        var username = await SeedAsync();
        using var client = factory.CreateApiClient();

        for (var attempt = 0; attempt < MaxAttempts - 1; attempt++)
        {
            await SignInAsync(client, username, "definitely-not-the-password");
        }

        var allowed = await SignInAsync(client, username, LockoutFactory.SeedPassword);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        // If the count had NOT been reset, this run of failures would lock the
        // account and the final sign-in would be refused.
        for (var attempt = 0; attempt < MaxAttempts - 1; attempt++)
        {
            await SignInAsync(client, username, "definitely-not-the-password");
        }

        var stillAllowed = await SignInAsync(client, username, LockoutFactory.SeedPassword);

        Assert.Equal(HttpStatusCode.OK, stillAllowed.StatusCode);
    }

    [Fact]
    public async Task An_unknown_username_and_a_wrong_password_are_indistinguishable()
    {
        var username = await SeedAsync();
        using var client = factory.CreateApiClient();

        var unknown = await SignInAsync(client, $"u{Guid.NewGuid():N}"[..20], LockoutFactory.SeedPassword);
        var wrong = await SignInAsync(client, username, "definitely-not-the-password");

        var unknownProblem = await ProblemAssert.ProblemAsync(unknown, 401);
        var wrongProblem = await ProblemAssert.ProblemAsync(wrong, 401);

        // Everything but the per-request traceId must match, or the difference
        // itself answers "does this account exist".
        Assert.Equal(Comparable(unknownProblem), Comparable(wrongProblem));
    }

    /// <summary>The problem body minus the one member that legitimately differs per request.</summary>
    private static string Comparable(JsonElement problem) => string.Join(
        '|',
        problem.EnumerateObject()
            .Where(member => member.Name is not "traceId")
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .Select(member => $"{member.Name}={member.Value}"));

    private async Task<string> SeedAsync()
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        await factory.SeedUserAsync(
            username, LockoutFactory.SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);
        return username;
    }

    private static Task<HttpResponseMessage> SignInAsync(
        HttpClient client, string username, string password) =>
        client.PostAsJsonAsync(
            "/api/v1/identity/login", new { username, password }, CancellationToken.None);
}
