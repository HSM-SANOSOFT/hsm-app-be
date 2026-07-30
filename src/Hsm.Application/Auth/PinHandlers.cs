namespace Hsm.Application.Auth;

/// <summary>
/// The frozen PIN endpoints are inert stubs — generation/validation log and
/// persist nothing. Behavioral parity means preserving exactly that: an
/// authenticated 2xx no-op. (The real attempt-throttling/lockout behavior in
/// the frozen system lives in account recovery — see the ForgotPassword and
/// ResetPassword slices.)
///
/// Not a CQRS slice: there is no state to command and nothing to query, so
/// there is no request type to carry a policy. The two routes keep their edge
/// gate (authenticated, onboarding-completed) and call straight through.
/// </summary>
public static class PinHandlers
{
    public static Task GenerateAsync(string purpose, string target) => Task.CompletedTask;

    public static Task ValidateAsync(string purpose, string target, double code) => Task.CompletedTask;
}
