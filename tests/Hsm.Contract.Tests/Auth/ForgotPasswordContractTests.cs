namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// POST /v1/auth/password/forgot — non-enumerating, with the frozen
/// per-account throttle: at most FIVE requests per account per rolling hour
/// (account-recovery.service.ts MAX_REQUESTS_PER_HOUR = 5); the 429 is the
/// only non-generic outcome and its envelope carries only the code.
/// </summary>
public sealed class ForgotPasswordContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    private const string GenericMessage = "If an account exists, we have sent an email.";

    [Fact]
    public async Task Known_and_unknown_emails_get_the_same_generic_answer()
    {
        var (_, _, email, _) = await SeedPatientAsync();

        var known = await Api.PostJsonAsync(Client, "/v1/auth/password/forgot", new { email });
        var unknown = await Api.PostJsonAsync(
            Client, "/v1/auth/password/forgot", new { email = $"{Unique("ghost")}@contract.test" });

        AssertSuccessEnvelope(known, 201, "/v1/auth/password/forgot");
        AssertSuccessEnvelope(unknown, 201, "/v1/auth/password/forgot");
        Assert.Equal(GenericMessage, known.Data.GetProperty("message").GetString());
        Assert.Equal(GenericMessage, unknown.Data.GetProperty("message").GetString());

        // Only the known account got an email.
        Assert.Contains(Factory.Emailer.ResetTokens, t => t.Email == email);
    }

    [Fact]
    public async Task Sixth_request_within_the_hour_is_throttled_per_account()
    {
        var (_, _, email, _) = await SeedPatientAsync();

        // Frozen threshold: 5 per rolling hour succeed…
        for (var i = 0; i < 5; i++)
        {
            var ok = await Api.PostJsonAsync(Client, "/v1/auth/password/forgot", new { email });
            AssertSuccessEnvelope(ok, 201, "/v1/auth/password/forgot");
        }

        // …the sixth is 429, whose issue carries ONLY the stable code.
        var throttled = await Api.PostJsonAsync(Client, "/v1/auth/password/forgot", new { email });
        var issue = AssertErrorEnvelope(throttled, 429, "COMMON.TOO_MANY_REQUESTS");
        Assert.False(issue.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Invalid_email_shape_fails_validation()
    {
        var response = await Api.PostJsonAsync(
            Client, "/v1/auth/password/forgot", new { email = "not-an-email" });

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        var error = Assert.Single(issue.GetProperty("errors").EnumerateArray());
        Assert.Equal("email", error.GetProperty("field").GetString());
        Assert.Equal("isEmail", error.GetProperty("constraints")[0].GetString());
    }

    [Fact]
    public async Task Delivery_failure_stays_generic_and_returns_the_rate_limit_slot()
    {
        var (_, _, email, id) = await SeedPatientAsync();
        Factory.Emailer.FailNext = true;

        var response = await Api.PostJsonAsync(Client, "/v1/auth/password/forgot", new { email });

        // Non-enumeration holds even when enqueueing fails…
        AssertSuccessEnvelope(response, 201, "/v1/auth/password/forgot");
        Assert.Equal(GenericMessage, response.Data.GetProperty("message").GetString());
        // …and the failed token was compensated away (no lingering row).
        var rows = await Factory.WithDbAsync(db =>
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
                db.PasswordResetTokens.Where(t => t.UserId == id)));
        Assert.Equal(0, rows);
    }
}
