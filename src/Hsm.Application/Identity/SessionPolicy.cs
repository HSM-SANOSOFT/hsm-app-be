namespace Hsm.Application.Identity;

/// <summary>
/// How long a browser session lives, stated once. It is the cookie's
/// <c>ExpireTimeSpan</c> AND the horizon written into the session row, so the
/// two halves of a session — the cookie the browser holds and the row the
/// server holds — cannot drift into disagreeing about when it ended.
/// </summary>
public static class SessionPolicy
{
    /// <summary>
    /// Sliding: eight hours of INACTIVITY ends a session, roughly a shift. An
    /// active session is renewed rather than cut off mid-task.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// How much of <see cref="Lifetime"/> must be left before the session row's
    /// expiry is pushed forward. Recomputing it on every request would be a
    /// database WRITE on every request; recomputing it only in the last half of
    /// the window costs at most one write every four hours per session and
    /// keeps the row ahead of the cookie either way — the cookie handler renews
    /// on the same halfway rule.
    /// </summary>
    public static TimeSpan ExtendWhenRemainingBelow => Lifetime / 2;
}
