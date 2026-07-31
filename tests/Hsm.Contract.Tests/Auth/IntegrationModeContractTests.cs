using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// The second auth mode: long-lived bearer tokens for machine consumers,
/// admin-provisioned, with a token store SEPARATE from browser sessions
/// (frozen signupIntegration / logoutIntegration and the two token entities).
/// </summary>
public sealed class IntegrationModeContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    private async Task<(ApiResponse Response, string AdminAccess)> ProvisionAsync()
    {
        var (username, password, _, _) = await SeedAdminAsync();
        var adminLogin = await LoginAsync(username, password);
        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/signup/integration",
            new { name = Unique("machine"), description = "contract test consumer", functionality = "dev" },
            bearer: adminLogin.AccessToken);
        return (response, adminLogin.AccessToken);
    }

    [Fact]
    public async Task Admin_provisions_an_integration_and_gets_tokens_without_cookies()
    {
        var (response, _) = await ProvisionAsync();

        AssertSuccessEnvelope(response, 201, "/v1/auth/signup/integration");
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        // Machine consumers never get cookie transport.
        Assert.Empty(response.SetCookies);
    }

    [Fact]
    public async Task Integration_token_lifetimes_are_1d_access_and_30d_refresh()
    {
        var (response, _) = await ProvisionAsync();

        var access = Api.DecodeJwtPayload(response.AccessToken);
        var refresh = Api.DecodeJwtPayload(response.RefreshToken);
        Assert.Equal(24 * 60 * 60, access.GetProperty("exp").GetInt64() - access.GetProperty("iat").GetInt64());
        Assert.Equal(30 * 24 * 60 * 60, refresh.GetProperty("exp").GetInt64() - refresh.GetProperty("iat").GetInt64());
    }

    [Fact]
    public async Task Integration_bearer_authenticates_without_a_session_cookie()
    {
        var (provisioned, _) = await ProvisionAsync();

        var profile = await Api.GetAsync(Client, "/v1/auth/profile", bearer: provisioned.AccessToken);

        AssertSuccessEnvelope(profile, 200, "/v1/auth/profile");
        var data = profile.Data;
        Assert.Equal(
            "integration",
            Assert.Single(data.GetProperty("roles").EnumerateArray()).GetString());
        Assert.StartsWith("machine_", data.GetProperty("name").GetString(), StringComparison.Ordinal);
        // Integration principals have no user-only claims.
        Assert.False(data.TryGetProperty("username", out _));
        Assert.False(data.TryGetProperty("onboardingCompletedAt", out _));
        Assert.True(data.TryGetProperty("iat", out _));
        Assert.True(data.TryGetProperty("exp", out _));
    }

    [Fact]
    public async Task Token_stores_are_separate_between_the_two_modes()
    {
        var (provisioned, _) = await ProvisionAsync();
        var integrationId = Guid.Parse(
            Api.DecodeJwtPayload(provisioned.AccessToken).GetProperty("sub").GetString()!);

        // The integration's active token lives ONLY in the integration store.
        var integrationRows = await Factory.WithDbAsync(db =>
            db.IntegrationRefreshTokens.CountAsync(t => t.IntegrationAccountId == integrationId && t.IsActive));
        var userRows = await Factory.WithDbAsync(db =>
            db.UserRefreshTokens.CountAsync(t => t.UserId == integrationId));
        Assert.Equal(1, integrationRows);
        Assert.Equal(0, userRows);
    }

    [Fact]
    public async Task Integration_refresh_rotates_only_the_integration_store()
    {
        var (provisioned, _) = await ProvisionAsync();

        var refreshed = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: provisioned.RefreshToken);
        AssertSuccessEnvelope(refreshed, 200, "/v1/auth/refresh");

        // Rotation revoked the original integration refresh token.
        var replay = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: provisioned.RefreshToken);
        var issue = AssertErrorEnvelope(replay, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Refresh token is not valid", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Admin_signs_out_an_integration_by_token()
    {
        var (provisioned, adminAccess) = await ProvisionAsync();

        var logout = await Api.PostJsonAsync(
            Client, "/v1/auth/logout/integration", new { token = provisioned.RefreshToken }, bearer: adminAccess);
        AssertSuccessEnvelope(logout, 201, "/v1/auth/logout/integration");
        Assert.False(logout.HasData);

        // The integration refresh token no longer has an active row.
        var replay = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: provisioned.RefreshToken);
        var replayIssue = AssertErrorEnvelope(replay, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Active Refresh token not found", replayIssue.GetProperty("message").GetString());

        // Signing out twice: 400 already logged out.
        var again = await Api.PostJsonAsync(
            Client, "/v1/auth/logout/integration", new { token = provisioned.RefreshToken }, bearer: adminAccess);
        var againIssue = AssertErrorEnvelope(again, 400, "COMMON.VALIDATION");
        Assert.Equal("already logged out", againIssue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Integration_logout_rejects_a_non_integration_token()
    {
        var (username, password, _, _) = await SeedAdminAsync();
        var adminLogin = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client, "/v1/auth/logout/integration", new { token = adminLogin.AccessToken }, bearer: adminLogin.AccessToken);

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Not an integration token", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Non_admin_cannot_provision_integrations()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/signup/integration",
            new { name = "nope", description = "nope", functionality = "dev" },
            bearer: login.AccessToken);

        var issue = AssertErrorEnvelope(response, 403, "COMMON.FORBIDDEN");
        // Task 15: the role check moved from the edge onto
        // SignupIntegrationCommand's [RequireRole(Roles.Admin)], and the
        // pipeline's refusal carries no message — only the status and the
        // stable code, both asserted above, are contract.
        Assert.False(issue.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Unauthenticated_provisioning_is_401()
    {
        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/signup/integration",
            new { name = "nope", description = "nope", functionality = "dev" });

        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Functionality_must_be_a_frozen_enum_value()
    {
        var (username, password, _, _) = await SeedAdminAsync();
        var adminLogin = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/signup/integration",
            new { name = "x", description = "y", functionality = "yolo" },
            bearer: adminLogin.AccessToken);

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        var error = Assert.Single(issue.GetProperty("errors").EnumerateArray());
        Assert.Equal("functionality", error.GetProperty("field").GetString());
        Assert.Equal("isEnum", error.GetProperty("constraints")[0].GetString());
    }
}
