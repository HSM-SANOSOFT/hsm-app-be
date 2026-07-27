using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// POST /v1/auth/password/reset — frozen token semantics: 1-hour TTL,
/// single-use, invalid/expired/used all fail with the SAME generic message,
/// and a successful reset revokes active sessions.
/// </summary>
public sealed class ResetPasswordContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    private const string GenericFailure = "Invalid or expired reset token";
    private const string NewPassword = "Reset-Passw0rd";

    private async Task<string> RequestTokenAsync(string email)
    {
        var before = Factory.Emailer.ResetTokens.Count;
        var response = await Api.PostJsonAsync(Client, "/v1/auth/password/forgot", new { email });
        AssertSuccessEnvelope(response, 201, "/v1/auth/password/forgot");
        var captured = Factory.Emailer.ResetTokens.Skip(before).Single(t => t.Email == email);
        return captured.Token;
    }

    [Fact]
    public async Task Valid_token_resets_the_password()
    {
        var (username, password, email, _) = await SeedPatientAsync();
        var token = await RequestTokenAsync(email);

        var response = await Api.PostJsonAsync(
            Client, "/v1/auth/password/reset", new { token, newPassword = NewPassword });

        AssertSuccessEnvelope(response, 201, "/v1/auth/password/reset");
        Assert.Equal("Password updated.", response.Data.GetProperty("message").GetString());

        var oldLogin = await LoginAsync(username, password);
        AssertErrorEnvelope(oldLogin, 401, "AUTH.INVALID_CREDENTIALS");
        var newLogin = await LoginAsync(username, NewPassword);
        AssertSuccessEnvelope(newLogin, 201, "/v1/auth/login");
    }

    [Fact]
    public async Task Reset_revokes_active_sessions()
    {
        var (username, password, email, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);
        var token = await RequestTokenAsync(email);

        await Api.PostJsonAsync(Client, "/v1/auth/password/reset", new { token, newPassword = NewPassword });

        // A session stolen before the reset cannot outlive the password change.
        var replay = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: login.RefreshToken);
        var issue = AssertErrorEnvelope(replay, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Active Refresh token not found", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Token_is_single_use()
    {
        var (_, _, email, _) = await SeedPatientAsync();
        var token = await RequestTokenAsync(email);
        await Api.PostJsonAsync(Client, "/v1/auth/password/reset", new { token, newPassword = NewPassword });

        var second = await Api.PostJsonAsync(
            Client, "/v1/auth/password/reset", new { token, newPassword = "Another-Passw0rd" });

        var issue = AssertErrorEnvelope(second, 400, "COMMON.VALIDATION");
        Assert.Equal(GenericFailure, issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Expired_token_fails_with_the_same_generic_message()
    {
        var (_, _, email, id) = await SeedPatientAsync();
        var token = await RequestTokenAsync(email);
        // Frozen TTL is one hour: age the token past it.
        await Factory.WithDbAsync(db => db.PasswordResetTokens
            .Where(t => t.UserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(
                t => t.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1))));

        var response = await Api.PostJsonAsync(
            Client, "/v1/auth/password/reset", new { token, newPassword = NewPassword });

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal(GenericFailure, issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Unknown_token_fails_with_the_same_generic_message()
    {
        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/password/reset",
            new { token = new string('a', 64), newPassword = NewPassword });

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal(GenericFailure, issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Short_new_password_fails_validation_before_touching_the_token()
    {
        var response = await Api.PostJsonAsync(
            Client, "/v1/auth/password/reset", new { token = "whatever", newPassword = "short" });

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        var error = Assert.Single(issue.GetProperty("errors").EnumerateArray());
        Assert.Equal("newPassword", error.GetProperty("field").GetString());
    }
}
