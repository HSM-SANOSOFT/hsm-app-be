namespace Hsm.Domain.Identity;

/// <summary>
/// Active-session record for a human user: the bcrypt hash of the latest
/// refresh token. Exactly one row per user is active; rotation deactivates
/// the previous row and inserts a new one (frozen AuthService.refreshToken).
/// Deliberately a distinct type/store from
/// <see cref="IntegrationRefreshToken"/> — the two auth modes never share a
/// token store.
/// </summary>
public class UserRefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Bcrypt hash of the refresh token JWT — never plaintext.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Active-token record for an integration account. Separate store from
/// <see cref="UserRefreshToken"/> by design (frozen behavioral separation).
/// </summary>
public class IntegrationRefreshToken
{
    public Guid Id { get; set; }
    public Guid IntegrationAccountId { get; set; }

    /// <summary>Bcrypt hash of the refresh token JWT — never plaintext.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
