using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Auth.Commands.SignupIntegration;
using Hsm.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Api.Tests.Identity;

public sealed class IntegrationTokenFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_integration_tokens";

    /// <summary>
    /// Provisions an integration account and returns its issued pair.
    ///
    /// <para>In process, through the handler, because Task 13 retired
    /// <c>POST /v1/auth/signup/integration</c> and Task 14 has not yet added
    /// <c>POST /api/v1/identity/integrations/register</c>. That is not a loss
    /// of coverage for the capability: the admin screen provisions accounts
    /// exactly this way (<c>IntegrationAccountsUiService.ProvisionAsync</c>
    /// dispatches the same command in the shell's own process), and
    /// <c>AuthRequestPolicyTests</c> pins the command's admin-only policy
    /// separately.</para>
    /// </summary>
    public async Task<TokenPair> ProvisionIntegrationAsync()
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IRequestHandler<SignupIntegrationCommand, TokenPair>>();
        return await handler.HandleAsync(
            new SignupIntegrationCommand($"machine_{Guid.NewGuid():N}", "refresh test", "dev"),
            CancellationToken.None);
    }

    /// <summary>
    /// Signs a refresh token for a HUMAN account — a credential no route hands
    /// out any more, and the point of the test that uses it.
    /// </summary>
    public async Task<string> HumanRefreshTokenAsync()
    {
        var username = $"s{Guid.NewGuid():N}"[..20];
        var id = await SeedUserAsync(username, SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);

        using var scope = Services.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IAuthTokenCodec>();
        return codec.Sign(
            new AuthPrincipal
            {
                Id = id.ToString(),
                Username = username,
                Roles = [Roles.Doctor],
                OnboardingCompletedAt = DateTimeOffset.UtcNow.ToString("O"),
                HasOnboardingClaim = true,
            },
            TokenKind.Refresh,
            TokenLifetimes.UserRefresh);
    }
}

/// <summary>
/// Integration accounts are the one caller that cannot hold a browser cookie,
/// so the JWT pair — and the rotation that keeps a long-lived credential from
/// being a permanent one — is still theirs until Task 14 replaces the refresh
/// token with an opaque value.
///
/// <para>These tests exist because Task 12 nearly deleted
/// <c>GET /v1/auth/refresh</c> as "browser machinery". It was not: the shell's
/// integration-accounts screen hands the operator a refresh token, and this is
/// the only route that redeems one. Task 13 kept the route alive on the same
/// reasoning — see <c>IntegrationRefreshEndpoint</c>.</para>
/// </summary>
public class IntegrationTokenTests(IntegrationTokenFactory factory)
    : IClassFixture<IntegrationTokenFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_integration_refresh_token_rotates_into_a_fresh_pair()
    {
        var issued = await factory.ProvisionIntegrationAsync();
        using var client = factory.CreateApiClient();

        var refreshed = await RefreshAsync(client, issued.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.False(string.IsNullOrEmpty(refreshed.AccessToken));
        Assert.False(string.IsNullOrEmpty(refreshed.RefreshToken));
        Assert.NotEqual(issued.RefreshToken, refreshed.RefreshToken);

        // Rotation means the OLD token stops working — otherwise a leaked
        // refresh token is valid for its full 30 days no matter what.
        var replayed = await RefreshAsync(client, issued.RefreshToken);
        await ProblemAssert.ProblemAsync(replayed.Response, 401);
    }

    [Fact]
    public async Task A_rotated_access_token_still_authenticates_the_integration()
    {
        var issued = await factory.ProvisionIntegrationAsync();
        using var client = factory.CreateApiClient();
        var refreshed = await RefreshAsync(client, issued.RefreshToken);

        using var bearerClient = factory.CreateApiClient();
        bearerClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {refreshed.AccessToken}");

        // An authenticated, non-role-gated read: 200 proves the rotated token
        // carried an id, a role and the integration's onboarding exemption all
        // the way through the pipeline.
        var response = await bearerClient.GetAsync("/api/v1/templates", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_integration_has_no_user_profile_to_read()
    {
        // GET /api/v1/identity/me reads the USER row, and an integration's
        // subject is an integration row. 404 rather than a synthesised profile:
        // the frozen /v1/auth/profile answered off the token's claims and would
        // have made a machine account look like a person.
        var issued = await factory.ProvisionIntegrationAsync();
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {issued.AccessToken}");

        var response = await client.GetAsync("/api/v1/identity/me", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    [Fact]
    public async Task Refreshing_without_a_bearer_token_is_401()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/v1/auth/refresh", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task A_humans_refresh_token_is_refused_by_the_integration_only_route()
    {
        // The escalation this closes: rotation re-signs the presented token's
        // OWN claims without reading the user row, so a doctor demoted to nurse
        // — whose cookie session the security-stamp bump correctly kills —
        // could otherwise trade a refresh token for a fresh access token still
        // claiming `doctor`, indefinitely.
        //
        // Task 13 narrowed the exposure further: registering and completing
        // onboarding return the user row now, so NO route hands a human one of
        // these any more. The token below is signed directly for that reason —
        // the handler's refusal must not quietly depend on there being no way
        // to obtain the credential.
        var humanRefreshToken = await factory.HumanRefreshTokenAsync();
        using var client = factory.CreateApiClient();

        var response = await RefreshAsync(client, humanRefreshToken);

        // 403, not 401: the token is valid and was verified; what is refused is
        // the capability, so a client must not retry by re-authenticating.
        await ProblemAssert.ProblemAsync(response.Response, 403);
    }

    [Fact]
    public async Task An_access_token_cannot_be_used_as_a_refresh_token()
    {
        // The two families are signed with different secrets, and the refresh
        // route validates against the refresh one only.
        var issued = await factory.ProvisionIntegrationAsync();
        using var client = factory.CreateApiClient();

        var response = await RefreshAsync(client, issued.AccessToken);

        await ProblemAssert.ProblemAsync(response.Response, 401);
    }

    private static async Task<RefreshOutcome> RefreshAsync(HttpClient client, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/auth/refresh");
        request.Headers.Add("Authorization", $"Bearer {refreshToken}");
        var response = await client.SendAsync(request, CancellationToken.None);
        if (!response.IsSuccessStatusCode)
        {
            return new RefreshOutcome(response, string.Empty, string.Empty);
        }

        // Flat, un-enveloped: the frozen response envelope is gone.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        return new RefreshOutcome(
            response,
            body.GetProperty("access_token").GetString()!,
            body.GetProperty("refresh_token").GetString()!);
    }

    private sealed record RefreshOutcome(
        HttpResponseMessage Response, string AccessToken, string RefreshToken)
    {
        public HttpStatusCode Status => Response.StatusCode;
    }
}
