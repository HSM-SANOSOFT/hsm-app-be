namespace Hsm.Domain.Identity;

/// <summary>
/// A single-use, expiring password-reset token. Only the SHA-256 hash of the
/// 256-bit plaintext is persisted; the plaintext link is emailed and never
/// stored. Spent by stamping <see cref="UsedAt"/>; invalid once used or past
/// <see cref="ExpiresAt"/> (frozen PasswordResetTokenEntity semantics).
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
