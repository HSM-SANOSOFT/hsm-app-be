namespace Hsm.Domain.Identity;

/// <summary>
/// Active-token record for an integration account, and — since the identity
/// rewrite — the only refresh token in the system. Its human counterpart went
/// with the JWT session: browsers hold a sliding Identity cookie, revoked by
/// the security stamp rather than by a stored hash, so there is nothing left
/// for a user refresh-token row to record.
///
/// <para>Exactly one row per account is active; rotation deactivates the
/// previous row and inserts a new one, which is what makes a leaked token stop
/// working at the next refresh.</para>
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
