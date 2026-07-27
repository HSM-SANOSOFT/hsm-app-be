using System.Text.Json;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// POST /v1/auth/onboarding — forced first-login onboarding for admin-created
/// staff (frozen completeOnboarding + OnboardingGuard): pending users reach
/// only @AllowPending routes; completion reissues tokens and revokes the
/// pre-onboarding refresh token.
/// </summary>
public sealed class OnboardingContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    private const string TempPassword = "Temp-Passw0rd";
    private const string NewPassword = "New-Passw0rd";

    private async Task<(string Username, string Email, ApiResponse Login)> SeedPendingStaffAsync()
    {
        var username = Unique("staff");
        await Factory.SeedUserAsync(username, TempPassword, "doctor", onboardingCompletedAt: null);
        var login = await LoginAsync(username, TempPassword);
        return (username, $"{username}@contract.test", login);
    }

    [Fact]
    public async Task Pending_user_is_blocked_from_feature_routes_but_reaches_profile()
    {
        var (_, _, login) = await SeedPendingStaffAsync();

        // A non-@AllowPending route: blocked with the frozen message.
        var blocked = await Api.PostJsonAsync(
            Client,
            "/v1/auth/pin/generate",
            new { purpose = "email_verification", target = "x@contract.test" },
            bearer: login.AccessToken);
        var issue = AssertErrorEnvelope(blocked, 403, "COMMON.FORBIDDEN");
        Assert.Equal(
            "Onboarding required: complete first-login onboarding to continue",
            issue.GetProperty("message").GetString());

        // @AllowPending routes stay reachable so completion can't deadlock.
        var profile = await Api.GetAsync(Client, "/v1/auth/profile", bearer: login.AccessToken);
        AssertSuccessEnvelope(profile, 200, "/v1/auth/profile");
        Assert.Equal(JsonValueKind.Null, profile.Data.GetProperty("onboardingCompletedAt").ValueKind);

        var refresh = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: login.RefreshToken);
        AssertSuccessEnvelope(refresh, 200, "/v1/auth/refresh");
    }

    [Fact]
    public async Task Completing_onboarding_reissues_tokens_and_clears_the_flag()
    {
        var (username, email, login) = await SeedPendingStaffAsync();

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/onboarding",
            new { newPassword = NewPassword, phoneNumber = "+593999999999", confirmEmail = email },
            bearer: login.AccessToken);

        AssertSuccessEnvelope(response, 201, "/v1/auth/onboarding");
        var payload = Api.DecodeJwtPayload(response.AccessToken);
        Assert.Equal(JsonValueKind.String, payload.GetProperty("onboardingCompletedAt").ValueKind);

        // The reissued session reaches feature routes.
        var nowAllowed = await Api.PostJsonAsync(
            Client,
            "/v1/auth/pin/generate",
            new { purpose = "email_verification", target = "x@contract.test" },
            bearer: response.AccessToken);
        AssertSuccessEnvelope(nowAllowed, 201, "/v1/auth/pin/generate");

        // The new password is live.
        var relogin = await LoginAsync(username, NewPassword);
        AssertSuccessEnvelope(relogin, 201, "/v1/auth/login");
        var oldPassword = await LoginAsync(username, TempPassword);
        AssertErrorEnvelope(oldPassword, 401, "AUTH.INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Completion_revokes_the_pre_onboarding_refresh_token()
    {
        var (_, email, login) = await SeedPendingStaffAsync();

        await Api.PostJsonAsync(
            Client,
            "/v1/auth/onboarding",
            new { newPassword = NewPassword, phoneNumber = "+593999999999", confirmEmail = email },
            bearer: login.AccessToken);

        var replay = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: login.RefreshToken);
        var issue = AssertErrorEnvelope(replay, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Refresh token is not valid", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Confirm_email_must_match_the_account_email()
    {
        var (_, _, login) = await SeedPendingStaffAsync();

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/onboarding",
            new { newPassword = NewPassword, phoneNumber = "+593999999999", confirmEmail = "wrong@contract.test" },
            bearer: login.AccessToken);

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal(
            "Confirmation email does not match the account email",
            issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Confirm_email_match_is_case_insensitive()
    {
        var (_, email, login) = await SeedPendingStaffAsync();

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/onboarding",
            new
            {
                newPassword = NewPassword,
                phoneNumber = "+593999999999",
                confirmEmail = email.ToUpperInvariant(),
            },
            bearer: login.AccessToken);

        AssertSuccessEnvelope(response, 201, "/v1/auth/onboarding");
    }

    [Fact]
    public async Task Already_completed_accounts_are_rejected()
    {
        var (username, password, email, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/onboarding",
            new { newPassword = NewPassword, phoneNumber = "+593999999999", confirmEmail = email },
            bearer: login.AccessToken);

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal("Onboarding already completed", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Short_new_password_fails_validation()
    {
        var (_, email, login) = await SeedPendingStaffAsync();

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/onboarding",
            new { newPassword = "short", phoneNumber = "+593999999999", confirmEmail = email },
            bearer: login.AccessToken);

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        var error = Assert.Single(issue.GetProperty("errors").EnumerateArray());
        Assert.Equal("newPassword", error.GetProperty("field").GetString());
        Assert.Contains(
            "minLength",
            error.GetProperty("constraints").EnumerateArray().Select(c => c.GetString()).ToList());
    }
}
