namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// POST /v1/auth/login — behavior pinned from the frozen auth.controller /
/// auth.service / auth-cookie.util at freeze/typescript-2026-07-27.
/// </summary>
public sealed class LoginContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Valid_credentials_issue_a_token_pair_in_the_success_envelope()
    {
        var (username, password, _, _) = await SeedPatientAsync();

        var response = await LoginAsync(username, password);

        AssertSuccessEnvelope(response, 201, "/v1/auth/login");
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.False(string.IsNullOrEmpty(response.RefreshToken));
    }

    [Fact]
    public async Task Login_sets_the_frozen_auth_cookies()
    {
        var (username, password, _, _) = await SeedPatientAsync();

        var response = await LoginAsync(username, password);

        // access_token: httpOnly, SameSite=Lax, path=/, 15 minutes.
        var access = response.SetCookieFor("access_token");
        Assert.NotNull(access);
        Assert.Contains("httponly", access, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", access, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", access, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=900", access, StringComparison.OrdinalIgnoreCase);

        // refresh_token: httpOnly, SameSite=Strict, path-scoped to /v1/auth, 1 day.
        var refresh = response.SetCookieFor("refresh_token");
        Assert.NotNull(refresh);
        Assert.Contains("httponly", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/auth", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=86400", refresh, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Browser_token_lifetimes_are_15m_access_and_1d_refresh()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var response = await LoginAsync(username, password);

        var access = Api.DecodeJwtPayload(response.AccessToken);
        var refresh = Api.DecodeJwtPayload(response.RefreshToken);
        Assert.Equal(15 * 60, access.GetProperty("exp").GetInt64() - access.GetProperty("iat").GetInt64());
        Assert.Equal(24 * 60 * 60, refresh.GetProperty("exp").GetInt64() - refresh.GetProperty("iat").GetInt64());
    }

    [Fact]
    public async Task Unknown_username_and_wrong_password_surface_the_same_coded_401()
    {
        var (username, _, _, _) = await SeedPatientAsync();

        var unknown = await LoginAsync(Unique("nobody"), "whatever-pass");
        var wrongPassword = await LoginAsync(username, "wrong-password");

        var unknownIssue = AssertErrorEnvelope(unknown, 401, "AUTH.INVALID_CREDENTIALS");
        var wrongIssue = AssertErrorEnvelope(wrongPassword, 401, "AUTH.INVALID_CREDENTIALS");
        Assert.Equal("Invalid credentials", unknownIssue.GetProperty("message").GetString());
        Assert.Equal("Invalid password", wrongIssue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Missing_credentials_fail_as_401_not_400()
    {
        // The frozen local guard ran before body validation.
        var response = await Api.PostJsonAsync(Client, "/v1/auth/login", new { username = "someone" });

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Unauthorized", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Valid_credentials_with_unknown_body_property_still_fail_validation()
    {
        // Guard → validation-pipe order: creds pass, whitelist still rejects.
        var (username, password, _, _) = await SeedPatientAsync();

        var response = await Api.PostJsonAsync(
            Client, "/v1/auth/login", new { username, password, unexpected = "x" });

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        var error = Assert.Single(issue.GetProperty("errors").EnumerateArray());
        Assert.Equal("unexpected", error.GetProperty("field").GetString());
        Assert.Equal("whitelistValidation", error.GetProperty("constraints")[0].GetString());
    }

    [Fact]
    public async Task Session_from_login_authenticates_a_subsequent_request()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        // Bearer transport (integration/direct-API style).
        var viaBearer = await Api.GetAsync(Client, "/v1/auth/profile", bearer: login.AccessToken);
        AssertSuccessEnvelope(viaBearer, 200, "/v1/auth/profile");
        Assert.Equal(username, viaBearer.Data.GetProperty("username").GetString());

        // Cookie transport (browser/SSR style).
        var viaCookie = await Api.GetAsync(
            Client, "/v1/auth/profile", cookies: [("access_token", login.AccessToken)]);
        AssertSuccessEnvelope(viaCookie, 200, "/v1/auth/profile");
        Assert.Equal(username, viaCookie.Data.GetProperty("username").GetString());
    }
}
