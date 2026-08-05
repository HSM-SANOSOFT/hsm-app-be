using Hsm.Domain.Identity;

namespace Hsm.Api.Identity;

/// <summary>
/// The identity module's wire shapes. There is no DTO layer behind these: the
/// records below ARE the allow-list, and the fields an <see cref="HsmUser"/>
/// carries that are not named here — the password hash, the security stamp,
/// the lockout counters, the soft-delete marker — cannot reach a caller by
/// accident because nothing maps them.
/// </summary>
public sealed record MeResource(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string? SecondName,
    string FirstLastName,
    string? SecondLastName,
    string? PhoneNumber,
    IReadOnlyList<string> Roles,
    DateTimeOffset? OnboardingCompletedAt)
{
    /// <summary>
    /// Roles are a parameter rather than a property of the user: Identity keeps
    /// assignments in their own table, so only the caller knows whether it has
    /// already paid for them.
    /// </summary>
    public static MeResource From(HsmUser user, IReadOnlyList<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new MeResource(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.FirstName,
            user.SecondName,
            user.FirstLastName,
            user.SecondLastName,
            user.PhoneNumber,
            roles,
            user.OnboardingCompletedAt);
    }
}

/// <summary>
/// Public self-registration. A <c>roles</c> member is deliberately absent
/// rather than ignored: registration always provisions a Patient, and a shape
/// that cannot carry a role is a stronger guarantee of that than a handler
/// that discards one.
/// </summary>
public sealed record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string FirstName,
    string FirstLastName,
    string? SecondName,
    string? SecondLastName,
    string? PhoneNumber,
    string? Gender);

public sealed record LoginRequest(string Username, string Password);

public sealed record OnboardingRequest(string NewPassword, string PhoneNumber, string ConfirmEmail);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record RecoverUsernameRequest(string Email);

public sealed record AntiforgeryTokenResource(string Token);

/// <summary>
/// The body of the two enumeration-safe 202s. It carries a fixed message and
/// nothing else on purpose — anything derived from the request (the address,
/// whether an account matched, how long the send took) would be the very
/// oracle those routes exist to withhold.
/// </summary>
public sealed record AcknowledgedResource(string Message);
