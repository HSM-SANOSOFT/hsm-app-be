using Hsm.Application.Auth;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// GET /v1/auth/refresh — rotation semantics: the presented refresh token
/// must match the single active hashed row; success rotates so the prior
/// token stops working (frozen validateRefreshToken + refresh).
/// </summary>
public sealed class RefreshContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Refresh_rotates_the_pair_and_revokes_the_prior_token()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        // Rotate via the refresh cookie (browser transport).
        var refreshed = await Api.GetAsync(
            Client, "/v1/auth/refresh", cookies: [("refresh_token", login.RefreshToken)]);
        AssertSuccessEnvelope(refreshed, 200, "/v1/auth/refresh");
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.NotNull(refreshed.SetCookieFor("access_token"));

        // DoD C1: the prior refresh token is rejected after rotation.
        var replay = await Api.GetAsync(
            Client, "/v1/auth/refresh", cookies: [("refresh_token", login.RefreshToken)]);
        var issue = AssertErrorEnvelope(replay, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Refresh token is not valid", issue.GetProperty("message").GetString());

        // The rotated token still works.
        var again = await Api.GetAsync(
            Client, "/v1/auth/refresh", cookies: [("refresh_token", refreshed.RefreshToken)]);
        AssertSuccessEnvelope(again, 200, "/v1/auth/refresh");
    }

    [Fact]
    public async Task Refresh_works_over_the_bearer_header_too()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var refreshed = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: login.RefreshToken);

        AssertSuccessEnvelope(refreshed, 200, "/v1/auth/refresh");
    }

    [Fact]
    public async Task Refresh_after_logout_is_rejected_and_issues_nothing()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);
        await Api.GetAsync(Client, "/v1/auth/logout", bearer: login.AccessToken);

        var response = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: login.RefreshToken);

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Active Refresh token not found", issue.GetProperty("message").GetString());
        Assert.False(response.HasData);
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected_with_TOKEN_EXPIRED()
    {
        var (_, _, _, id) = await SeedPatientAsync();
        var principal = new AuthPrincipal { Id = id.ToString(), Roles = ["patient"] };
        var expired = Factory.SignToken(principal, TokenKind.Refresh, TimeSpan.FromMilliseconds(50));
        await Task.Delay(300);

        var response = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: expired);

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("token expired", issue.GetProperty("message").GetString());
        Assert.Equal("TOKEN_EXPIRED", issue.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Garbage_refresh_token_is_rejected_with_INVALID_TOKEN()
    {
        var response = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: "not-a-jwt");

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Invalid token", issue.GetProperty("message").GetString());
        Assert.Equal("INVALID_TOKEN", issue.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Missing_refresh_token_is_401_unauthorized()
    {
        var response = await Api.GetAsync(Client, "/v1/auth/refresh");

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Unauthorized", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Access_token_cannot_impersonate_a_refresh_token()
    {
        // Separate signing secrets: an access token presented to /refresh
        // fails signature validation.
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: login.AccessToken);

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Invalid token", issue.GetProperty("message").GetString());
    }
}
