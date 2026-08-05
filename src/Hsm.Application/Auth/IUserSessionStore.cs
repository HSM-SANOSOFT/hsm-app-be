namespace Hsm.Application.Auth;

/// <summary>
/// The open-session table behind per-session revocation. Four operations, and
/// the read is on the hot path of every cookie-authenticated request — it is a
/// primary-key lookup for exactly that reason.
/// </summary>
public interface IUserSessionStore
{
    /// <summary>
    /// Records a newly opened session, and reclaims the user's own expired rows
    /// in the same call — housekeeping that costs nothing extra here and keeps
    /// the table from growing without bound, without needing a scheduled job.
    /// </summary>
    Task OpenAsync(Guid sessionId, Guid userId, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>
    /// The session's recorded expiry, or <see langword="null"/> when no row
    /// exists — which is what a revoked, never-opened or reclaimed session all
    /// look like, deliberately.
    /// </summary>
    Task<DateTimeOffset?> ExpiresAtAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Pushes an open session's expiry out, as the cookie's own slides.</summary>
    Task ExtendAsync(Guid sessionId, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>Revokes one session. True when a row was actually removed.</summary>
    Task<bool> CloseAsync(Guid sessionId, CancellationToken ct = default);
}
