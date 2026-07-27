namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// The frozen per-IP throttle on the recovery routes: @Throttle long —
/// 10 requests per 60 seconds per route (auth.controller.ts). This class has
/// its own factory (fresh limiter window) and burns the budget on ONE route.
/// </summary>
public sealed class RecoveryRateLimitContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Eleventh_recovery_request_within_the_window_is_throttled()
    {
        var email = $"{Unique("ratelimit")}@contract.test";

        for (var i = 0; i < 10; i++)
        {
            var ok = await Api.PostJsonAsync(Client, "/v1/auth/username/recover", new { email });
            AssertSuccessEnvelope(ok, 201, "/v1/auth/username/recover");
        }

        var throttled = await Api.PostJsonAsync(Client, "/v1/auth/username/recover", new { email });

        // Like every frozen throttler rejection, the envelope carries only
        // the stable code.
        var issue = AssertErrorEnvelope(throttled, 429, "COMMON.TOO_MANY_REQUESTS");
        Assert.False(issue.TryGetProperty("message", out _));
    }
}
