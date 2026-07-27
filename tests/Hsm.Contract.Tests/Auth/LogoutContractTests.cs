using Hsm.Application.Auth;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// GET /v1/auth/logout — accepts access OR refresh token (expired accepted),
/// deactivates the user session rows, and always clears cookies.
/// </summary>
public sealed class LogoutContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Logout_succeeds_with_empty_envelope_and_clears_cookies()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.GetAsync(Client, "/v1/auth/logout", bearer: login.AccessToken);

        AssertSuccessEnvelope(response, 200, "/v1/auth/logout");
        // Void handler → envelope has metadata only, no data key.
        Assert.False(response.HasData);
        // Cleared cookies (expired) on the same paths they were set.
        var access = response.SetCookieFor("access_token");
        var refresh = response.SetCookieFor("refresh_token");
        Assert.NotNull(access);
        Assert.NotNull(refresh);
        Assert.Contains("path=/v1/auth", refresh, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Second_logout_is_400_already_logged_out()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);
        await Api.GetAsync(Client, "/v1/auth/logout", bearer: login.AccessToken);

        var response = await Api.GetAsync(Client, "/v1/auth/logout", bearer: login.AccessToken);

        // 400 maps to the frozen COMMON.VALIDATION fallback code.
        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal("already logged out", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Logout_without_any_token_is_401_but_still_clears_cookies()
    {
        var response = await Api.GetAsync(Client, "/v1/auth/logout");

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Token not found", issue.GetProperty("message").GetString());
        Assert.NotNull(response.SetCookieFor("access_token"));
        Assert.NotNull(response.SetCookieFor("refresh_token"));
    }

    [Fact]
    public async Task Logout_accepts_the_refresh_token_as_credential()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.GetAsync(
            Client, "/v1/auth/logout", cookies: [("refresh_token", login.RefreshToken)]);

        AssertSuccessEnvelope(response, 200, "/v1/auth/logout");
    }

    [Fact]
    public async Task Logout_accepts_an_expired_access_token()
    {
        // Sign-out must always be possible: verification ignores expiry.
        var (username, password, _, id) = await SeedPatientAsync();
        await LoginAsync(username, password);
        var principal = new AuthPrincipal { Id = id.ToString(), Roles = ["patient"] };
        var expired = Factory.SignToken(principal, TokenKind.Access, TimeSpan.FromMilliseconds(50));
        await Task.Delay(300);

        var response = await Api.GetAsync(Client, "/v1/auth/logout", bearer: expired);

        AssertSuccessEnvelope(response, 200, "/v1/auth/logout");
    }

    [Fact]
    public async Task Logout_with_an_unverifiable_token_is_401_invalid_token()
    {
        var response = await Api.GetAsync(Client, "/v1/auth/logout", bearer: "garbage.token.here");

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Invalid token", issue.GetProperty("message").GetString());
    }
}
