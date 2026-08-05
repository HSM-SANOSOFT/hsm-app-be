namespace Hsm.Application.Identity;

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
    /// The per-request check AND the sliding extension that goes with it, as ONE
    /// statement. True when every one of these still holds, false otherwise:
    ///
    /// <list type="bullet">
    /// <item>a row for <paramref name="sessionId"/> exists (a sign-out deletes
    /// it) and has not itself expired;</item>
    /// <item>it belongs to <paramref name="userId"/> — the caller presenting the
    /// cookie must be the account the session was opened for;</item>
    /// <item>that account is still LIVE and ACTIVE (<c>DeletedAt is null</c>,
    /// <c>IsActive</c>).</item>
    /// </list>
    ///
    /// <para>The account clause rides HERE rather than in a second query because
    /// this read already happens on every cookie-authenticated request. Without
    /// it, deactivating or soft-deleting a user leaves every session they
    /// already hold working for the rest of its sliding window: Identity's own
    /// security-stamp validator looks the row up through the UNFILTERED
    /// <c>UserManager.FindByIdAsync</c>, so it sees a soft-deleted account as a
    /// perfectly good one and never notices <c>IsActive</c> at all.</para>
    ///
    /// <para>The extension is unconditional, and that is the point — see
    /// <see cref="SessionPolicy"/>. Returning false leaves the row untouched.</para>
    /// </summary>
    Task<bool> TouchAsync(
        Guid sessionId, Guid userId, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>
    /// Pushes an open session's expiry out without the checks
    /// <see cref="TouchAsync"/> makes — for the sign-in paths that have just
    /// established who the caller is (<c>HsmSessionSignIn.RefreshAsync</c>).
    /// </summary>
    Task ExtendAsync(Guid sessionId, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>Revokes one session. True when a row was actually removed.</summary>
    Task<bool> CloseAsync(Guid sessionId, CancellationToken ct = default);
}
