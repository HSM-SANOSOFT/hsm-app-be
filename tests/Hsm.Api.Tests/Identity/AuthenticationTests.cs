using System.Net;
using System.Net.Http.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Identity;

public sealed class AuthenticationFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_authn";
}

public class AuthenticationTests(AuthenticationFactory factory)
    : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_session_cookie_is_http_only_strict_and_not_a_jwt()
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        await factory.SeedUserAsync(
            username, AuthenticationFactory.SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/identity/login",
            new { username, password = AuthenticationFactory.SeedPassword },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("hsm.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        // The session value is a protected blob, not a readable JWT.
        Assert.DoesNotContain("eyJ", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cookie_session_authenticates_a_subsequent_request()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_garbage_bearer_token_is_401_and_never_falls_back_to_a_cookie()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-token");

        var response = await client.GetAsync("/api/v1/users", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task Roles_reach_the_pipeline_from_identity_role_claims()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.GetAsync("/api/v1/users", CancellationToken.None);

        // Authenticated, wrong role — proves the role claim was read, not that
        // authentication failed.
        await ProblemAssert.ProblemAsync(response, 403);
    }

    [Fact]
    public async Task A_pending_onboarding_actor_is_refused_by_the_pipeline()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse, onboarded: false);

        var response = await client.GetAsync("/api/v1/templates", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }
}
