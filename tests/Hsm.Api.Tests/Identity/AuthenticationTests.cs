using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Api.Tests.Identity;

public sealed class AuthenticationFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_authn";

    /// <summary>
    /// Signs an access token for a real HUMAN account so a test can exercise the
    /// BEARER half of the adaptive scheme end to end. It goes through the host's
    /// own <see cref="IIntegrationTokenCodec"/> rather than hand-rolling a JWT,
    /// so the claim layout under test is the one the system actually issues.
    ///
    /// <para>No ROUTE signs a person one of these — people hold session cookies,
    /// and the codec's issuer refuses a non-integration principal outright. The
    /// signing primitive is reached directly here on purpose: the three tests
    /// below are about the bearer HANDLER reading roles and subject, and they
    /// need a role a machine account does not have (admin, nurse) to prove that
    /// the claims were weighed rather than the token merely accepted.</para>
    /// </summary>
    public async Task<(string Bearer, Guid UserId)> BearerForSeededUserAsync(
        string role, bool onboarded = true)
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        var id = await SeedUserAsync(
            username, SeedPassword, role, onboarded ? DateTimeOffset.UtcNow : null);

        using var scope = Services.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IIntegrationTokenCodec>();
        var principal = new AuthPrincipal
        {
            Id = id.ToString(),
            Username = username,
            Roles = [role],
            OnboardingCompletedAt = onboarded ? DateTimeOffset.UtcNow.ToString("O") : null,
            HasOnboardingClaim = true,
        };
        return (codec.Sign(principal, TimeSpan.FromMinutes(15)), id);
    }
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

    // ----- the BEARER half, end to end through the pipeline ----------------
    //
    // These three exist because the bearer handler maps no inbound claims and
    // names the frozen `roles`/`sub` claims itself. If that mapping is wrong,
    // a bearer caller still AUTHENTICATES — it just arrives with no id or no
    // roles — so nothing short of authorizing a role-gated command catches it.

    [Fact]
    public async Task A_valid_bearer_token_authorizes_a_role_gated_request()
    {
        var (bearer, _) = await factory.BearerForSeededUserAsync(Roles.Admin);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearer}");

        var response = await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        // 200 means the whole chain held: JWT → JwtBearer → ClaimsPrincipal →
        // RequestActorFactory.CreateAsync(ClaimsPrincipal) → an actor carrying
        // the `roles` claim → AuthorizationBehavior's [RequireRole(Admin)].
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_bearer_tokens_roles_are_read_rather_than_assumed()
    {
        var (bearer, _) = await factory.BearerForSeededUserAsync(Roles.Nurse);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearer}");

        var response = await client.GetAsync("/api/v1/users", CancellationToken.None);

        // 403, not 401 and not 200: the roles were parsed and WEIGHED. Without
        // this the test above could pass on an actor with no roles at all if
        // the pipeline ever stopped requiring them.
        await ProblemAssert.ProblemAsync(response, 403);
    }

    [Fact]
    public async Task A_bearer_actor_carries_the_tokens_own_subject_as_its_id()
    {
        var (bearer, userId) = await factory.BearerForSeededUserAsync(Roles.Admin);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearer}");

        // /me resolves the target from ICurrentPrincipal.Actor.Id and nothing
        // else, so the row it returns IS the actor's id — a stronger statement
        // than "some actor was installed". No antiforgery token is sent: a
        // bearer caller never needs one.
        var response = await client.PatchAsJsonAsync(
            "/api/v1/users/me",
            new { firstName = "Bearer" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(userId, body.GetProperty("id").GetGuid());
        Assert.Equal("Bearer", body.GetProperty("firstName").GetString());
    }
}
