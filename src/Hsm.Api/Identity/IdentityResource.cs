using Hsm.Domain.Identity;

namespace Hsm.Api.Identity;

/// <summary>
/// The identity module's wire shapes. Task 13 completes the set (register,
/// onboarding and the three recovery requests) as it moves the rest of the
/// <c>/v1/auth</c> surface here; the records below are the ones the
/// authentication mechanism itself needs.
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

public sealed record LoginRequest(string Username, string Password);

public sealed record AntiforgeryTokenResource(string Token);
