namespace Hsm.Domain.Identity;

/// <summary>
/// A human account. Mirrors the frozen users table: username/email are
/// case-insensitively unique, the password is a bcrypt hash, and
/// <see cref="OnboardingCompletedAt"/> null marks an admin-created staff
/// account still pending forced first-login onboarding.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Bcrypt hash — never plaintext.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string? SecondName { get; set; }
    public string FirstLastName { get; set; } = string.Empty;
    public string? SecondLastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Gender { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Null while the account is pending first-login onboarding. The database
    /// row is authoritative — never the JWT claim (frozen OnboardingGuard).
    /// </summary>
    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; }
    public bool PhoneVerified { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public List<UserRole> Roles { get; set; } = [];
}

/// <summary>A role assignment row: (user, domain, role) unique.</summary>
public class UserRole
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
