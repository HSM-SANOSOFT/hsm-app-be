namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// POST /v1/auth/pin/generate and /v1/auth/pin/validate — the frozen
/// implementation is an authenticated, validated NO-OP (both service methods
/// are stubs that persist nothing). Behavioral parity pins exactly that:
/// auth + payload validation + empty 2xx envelope. There are no frozen
/// PIN-attempt thresholds to port — the frozen lockout behavior lives in
/// account recovery (see ForgotPassword/ResetPassword tests).
/// </summary>
public sealed class PinContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Pin_generate_requires_authentication()
    {
        var response = await Api.PostJsonAsync(
            Client, "/v1/auth/pin/generate", new { purpose = "email_verification", target = "0912345678" });

        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Pin_generate_for_a_national_id_returns_an_empty_success()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/pin/generate",
            new { purpose = "identity_verification", target = "0912345678" },
            bearer: login.AccessToken);

        AssertSuccessEnvelope(response, 201, "/v1/auth/pin/generate");
        Assert.False(response.HasData);
    }

    [Fact]
    public async Task Pin_validate_is_the_same_authenticated_noop()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/pin/validate",
            new { purpose = "identity_verification", target = "0912345678", code = 123456 },
            bearer: login.AccessToken);

        AssertSuccessEnvelope(response, 201, "/v1/auth/pin/validate");
        Assert.False(response.HasData);
    }

    [Fact]
    public async Task Pin_purpose_must_be_a_frozen_enum_value()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/pin/generate",
            new { purpose = "world_domination", target = "0912345678" },
            bearer: login.AccessToken);

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        var error = Assert.Single(issue.GetProperty("errors").EnumerateArray());
        Assert.Equal("purpose", error.GetProperty("field").GetString());
        Assert.Equal("isEnum", error.GetProperty("constraints")[0].GetString());
    }

    [Fact]
    public async Task Pin_validate_requires_a_numeric_code()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/pin/validate",
            new { purpose = "identity_verification", target = "0912345678", code = "123456" },
            bearer: login.AccessToken);

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        var error = Assert.Single(issue.GetProperty("errors").EnumerateArray());
        Assert.Equal("code", error.GetProperty("field").GetString());
        Assert.Equal("isNumber", error.GetProperty("constraints")[0].GetString());
    }
}
