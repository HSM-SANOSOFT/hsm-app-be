using System.Text.Json;
using Hsm.Application.Auth;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// GET /v1/auth/profile and GET /v1/auth/csrf plus the CSRF double-submit
/// posture on mutations (frozen csrf.util.ts): only cookie-authenticated
/// browser mutations are protected; bearer clients are exempt.
/// </summary>
public sealed class ProfileAndCsrfContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Profile_returns_the_token_claims_with_iat_and_exp()
    {
        var (username, password, email, id) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var profile = await Api.GetAsync(Client, "/v1/auth/profile", bearer: login.AccessToken);

        AssertSuccessEnvelope(profile, 200, "/v1/auth/profile");
        var data = profile.Data;
        Assert.Equal(id.ToString(), data.GetProperty("id").GetString());
        Assert.Equal(username, data.GetProperty("username").GetString());
        Assert.Equal(email, data.GetProperty("email").GetString());
        Assert.Equal("Contract", data.GetProperty("firstName").GetString());
        Assert.Equal("Test", data.GetProperty("firstLastName").GetString());
        Assert.Equal("patient", Assert.Single(data.GetProperty("roles").EnumerateArray()).GetString());
        Assert.Equal(JsonValueKind.String, data.GetProperty("onboardingCompletedAt").ValueKind);
        Assert.True(data.GetProperty("iat").GetInt64() > 0);
        Assert.True(data.GetProperty("exp").GetInt64() > data.GetProperty("iat").GetInt64());
    }

    [Fact]
    public async Task Profile_without_a_token_is_401()
    {
        var response = await Api.GetAsync(Client, "/v1/auth/profile");
        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Profile_with_an_expired_token_is_401_TOKEN_EXPIRED()
    {
        var (_, _, _, id) = await SeedPatientAsync();
        var principal = new AuthPrincipal { Id = id.ToString(), Roles = ["patient"] };
        var expired = Factory.SignToken(principal, TokenKind.Access, TimeSpan.FromMilliseconds(50));
        await Task.Delay(300);

        var response = await Api.GetAsync(Client, "/v1/auth/profile", bearer: expired);

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("token expired", issue.GetProperty("message").GetString());
        Assert.Equal("TOKEN_EXPIRED", issue.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Csrf_endpoint_issues_a_token_and_the_frozen_cookie()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.GetAsync(
            Client, "/v1/auth/csrf", cookies: [("access_token", login.AccessToken)]);

        AssertSuccessEnvelope(response, 200, "/v1/auth/csrf");
        var token = response.Data.GetProperty("csrfToken").GetString();
        Assert.False(string.IsNullOrEmpty(token));
        var cookie = response.SetCookieFor("hsm.x-csrf-token");
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cookie_authenticated_mutation_without_csrf_header_is_403()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/pin/generate",
            new { purpose = "email_verification", target = "someone@contract.test" },
            cookies: [("access_token", login.AccessToken)]);

        // The frozen rejection surfaced from the middleware layer, outside
        // the envelope.
        Assert.Equal(403, response.Status);
        Assert.Contains("invalid csrf token", response.RawBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cookie_authenticated_mutation_with_the_csrf_token_passes()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);
        var csrf = await Api.GetAsync(
            Client, "/v1/auth/csrf", cookies: [("access_token", login.AccessToken)]);
        var csrfToken = csrf.Data.GetProperty("csrfToken").GetString()!;

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/pin/generate",
            new { purpose = "email_verification", target = "someone@contract.test" },
            cookies: [("access_token", login.AccessToken), ("hsm.x-csrf-token", csrfToken)],
            headers: [("x-csrf-token", csrfToken)]);

        AssertSuccessEnvelope(response, 201, "/v1/auth/pin/generate");
    }

    [Fact]
    public async Task Csrf_token_is_bound_to_the_session_that_minted_it()
    {
        var (userA, passwordA, _, _) = await SeedPatientAsync();
        var (userB, passwordB, _, _) = await SeedPatientAsync();
        var loginA = await LoginAsync(userA, passwordA);
        var loginB = await LoginAsync(userB, passwordB);
        var csrfA = await Api.GetAsync(
            Client, "/v1/auth/csrf", cookies: [("access_token", loginA.AccessToken)]);
        var tokenA = csrfA.Data.GetProperty("csrfToken").GetString()!;

        // Replay A's token under B's session: rejected.
        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/pin/generate",
            new { purpose = "email_verification", target = "someone@contract.test" },
            cookies: [("access_token", loginB.AccessToken), ("hsm.x-csrf-token", tokenA)],
            headers: [("x-csrf-token", tokenA)]);

        Assert.Equal(403, response.Status);
    }

    [Fact]
    public async Task Bearer_mutations_are_exempt_from_csrf()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/pin/generate",
            new { purpose = "email_verification", target = "someone@contract.test" },
            bearer: login.AccessToken);

        AssertSuccessEnvelope(response, 201, "/v1/auth/pin/generate");
    }
}
