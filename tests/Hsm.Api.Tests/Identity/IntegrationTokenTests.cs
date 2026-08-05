using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Identity;

public sealed class IntegrationTokenFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_integration_tokens";
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
/// the only route that redeems one.</para>
/// </summary>
public class IntegrationTokenTests(IntegrationTokenFactory factory)
    : IClassFixture<IntegrationTokenFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_integration_refresh_token_rotates_into_a_fresh_pair()
    {
        var issued = await ProvisionAsync();
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
        var issued = await ProvisionAsync();
        using var client = factory.CreateApiClient();
        var refreshed = await RefreshAsync(client, issued.RefreshToken);

        using var bearerClient = factory.CreateApiClient();
        bearerClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {refreshed.AccessToken}");
        var profile = await bearerClient.GetAsync("/v1/auth/profile", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var body = await profile.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Contains(
            Roles.Integration,
            body.GetProperty("data").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Refreshing_without_a_bearer_token_is_401()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/v1/auth/refresh", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task An_access_token_cannot_be_used_as_a_refresh_token()
    {
        // The two families are signed with different secrets, and the refresh
        // route validates against the refresh one only.
        var issued = await ProvisionAsync();
        using var client = factory.CreateApiClient();

        var response = await RefreshAsync(client, issued.AccessToken);

        await ProblemAssert.ProblemAsync(response.Response, 401);
    }

    /// <summary>Provisions an integration account through the admin route.</summary>
    private async Task<(string AccessToken, string RefreshToken)> ProvisionAsync()
    {
        using var admin = await factory.AuthenticatedClientAsync(Roles.Admin);
        var response = await admin.PostAsJsonAsync(
            "/v1/auth/signup/integration",
            new { name = $"machine_{Guid.NewGuid():N}", description = "refresh test", functionality = "dev" },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var data = body.GetProperty("data");
        return (data.GetProperty("access_token").GetString()!, data.GetProperty("refresh_token").GetString()!);
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

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var data = body.GetProperty("data");
        return new RefreshOutcome(
            response,
            data.GetProperty("access_token").GetString()!,
            data.GetProperty("refresh_token").GetString()!);
    }

    private sealed record RefreshOutcome(
        HttpResponseMessage Response, string AccessToken, string RefreshToken)
    {
        public HttpStatusCode Status => Response.StatusCode;
    }
}
