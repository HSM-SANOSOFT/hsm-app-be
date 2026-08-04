using Microsoft.AspNetCore.Identity;

namespace Hsm.Domain.Identity;

/// <summary>
/// A human account. Everything ASP.NET Core Identity already models — the id,
/// the login name and its normalized form, the email and its normalized form,
/// the password hash, the phone number, the confirmation flags, the security
/// stamp and the lockout counters — comes from <see cref="IdentityUser{TKey}"/>.
/// What is added here is what the hospital cares about and Identity does not.
///
/// <para>The frozen entity's columns map across as: Username → UserName,
/// EmailVerified → EmailConfirmed, PhoneVerified → PhoneNumberConfirmed. They
/// are not duplicated here; a second IsEmailVerified alongside EmailConfirmed
/// is exactly the kind of pair that drifts.</para>
///
/// <para><see cref="OnboardingCompletedAt"/> null marks an admin-created staff
/// account still pending forced first-login onboarding. The database row is
/// authoritative — never the claim; see RequestActorFactory.</para>
/// </summary>
public class HsmUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;

    public string? SecondName { get; set; }

    public string FirstLastName { get; set; } = string.Empty;

    public string? SecondLastName { get; set; }

    public string? Gender { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Null while the account is pending first-login onboarding.</summary>
    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Soft delete. Filtered out of the unique indexes, never on the wire.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
