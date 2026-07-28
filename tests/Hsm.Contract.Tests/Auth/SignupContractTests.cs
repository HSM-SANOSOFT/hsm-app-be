using System.Text.Json;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// POST /v1/auth/signup — public self-registration is ALWAYS a Patient
/// (role force-assignment), created onboarding-complete, with the frozen
/// ValidationPipe surface.
/// </summary>
public sealed class SignupContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    private static object ValidBody(string username) => new
    {
        username,
        email = $"{username}@contract.test",
        password = "Signup-Passw0rd",
        firstName = "Sign",
        firstLastName = "Up",
        phoneNumber = "+593999999999",
        gender = "other",
    };

    [Fact]
    public async Task Signup_creates_a_patient_and_returns_tokens_with_cookies()
    {
        var username = Unique("signup");

        var response = await Api.PostJsonAsync(Client, "/v1/auth/signup", ValidBody(username));

        AssertSuccessEnvelope(response, 201, "/v1/auth/signup");
        var payload = Api.DecodeJwtPayload(response.AccessToken);
        Assert.Equal("patient", Assert.Single(payload.GetProperty("roles").EnumerateArray()).GetString());
        // Patients are active immediately — onboarding is a staff-only flow.
        Assert.Equal(JsonValueKind.String, payload.GetProperty("onboardingCompletedAt").ValueKind);
        Assert.NotNull(response.SetCookieFor("access_token"));
        Assert.NotNull(response.SetCookieFor("refresh_token"));
    }

    [Fact]
    public async Task Client_supplied_roles_are_ignored_never_honored()
    {
        var username = Unique("escalate");
        var body = new
        {
            username,
            email = $"{username}@contract.test",
            password = "Signup-Passw0rd",
            firstName = "Priv",
            firstLastName = "Esc",
            roles = new[] { "admin", "developer" },
        };

        var response = await Api.PostJsonAsync(Client, "/v1/auth/signup", body);

        AssertSuccessEnvelope(response, 201, "/v1/auth/signup");
        var payload = Api.DecodeJwtPayload(response.AccessToken);
        Assert.Equal("patient", Assert.Single(payload.GetProperty("roles").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Missing_and_short_fields_fail_with_frozen_constraint_keys()
    {
        var response = await Api.PostJsonAsync(Client, "/v1/auth/signup", new
        {
            email = "someone@contract.test",
            password = "short",
            firstName = "A",
            firstLastName = "B",
        });

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal(JsonValueKind.Array, issue.GetProperty("message").ValueKind);
        var errors = issue.GetProperty("errors").EnumerateArray().ToList();

        var username = errors.Single(e => e.GetProperty("field").GetString() == "username");
        var usernameKeys = username.GetProperty("constraints").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Contains("isNotEmpty", usernameKeys);
        Assert.Contains("isString", usernameKeys);

        var password = errors.Single(e => e.GetProperty("field").GetString() == "password");
        Assert.Contains(
            "minLength",
            password.GetProperty("constraints").EnumerateArray().Select(c => c.GetString()).ToList());
        Assert.Contains(
            "password must be longer than or equal to 8 characters",
            issue.GetProperty("message").EnumerateArray().Select(m => m.GetString()).ToList());
    }

    [Fact]
    public async Task Unknown_properties_are_rejected()
    {
        var username = Unique("whitelist");
        var response = await Api.PostJsonAsync(Client, "/v1/auth/signup", new
        {
            username,
            email = $"{username}@contract.test",
            password = "Signup-Passw0rd",
            firstName = "W",
            firstLastName = "L",
            isAdmin = true,
        });

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        var error = Assert.Single(issue.GetProperty("errors").EnumerateArray());
        Assert.Equal("isAdmin", error.GetProperty("field").GetString());
        Assert.Equal("whitelistValidation", error.GetProperty("constraints")[0].GetString());
    }

    [Fact]
    public async Task Signed_up_user_can_immediately_use_the_session()
    {
        var username = Unique("fresh");
        var signup = await Api.PostJsonAsync(Client, "/v1/auth/signup", ValidBody(username));

        var profile = await Api.GetAsync(Client, "/v1/auth/profile", bearer: signup.AccessToken);

        AssertSuccessEnvelope(profile, 200, "/v1/auth/profile");
        Assert.Equal(username, profile.Data.GetProperty("username").GetString());
        Assert.Equal($"{username}@contract.test", profile.Data.GetProperty("email").GetString());
    }
}
