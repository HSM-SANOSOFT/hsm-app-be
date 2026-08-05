namespace Hsm.Domain.Identity;

/// <summary>
/// One OPEN browser session, and the thing that makes signing out mean
/// something.
///
/// <para>An Identity cookie is self-contained: deleting it from the browser
/// (all <c>SignOutAsync</c> can do on its own) leaves any copy taken beforehand
/// working until it expires. A row here is the server's half of the session —
/// the cookie names it by id, every cookie-authenticated request checks that
/// the row is still there, and signing out deletes it. Presence is the whole
/// test, so the failure mode is closed: no row, no session.</para>
///
/// <para>It is deliberately NOT the security stamp. The stamp is per USER, so
/// bumping it to end one session ends every session that user has open on every
/// device — correct for a password change or a role change, wrong for "log
/// out". This is per SESSION, which is what lets one workstation be signed out
/// while the same person stays signed in elsewhere.</para>
/// </summary>
public class UserSession
{
    /// <summary>The session id, minted at sign-in and carried as a claim on the cookie.</summary>
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this row stops standing for a live session. It tracks the COOKIE's
    /// own expiry rather than imposing a second, independent lifetime — see
    /// <c>HsmSessionValidator</c> for how it is pushed forward as the cookie
    /// slides, and why it is not simply recomputed on every request.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
