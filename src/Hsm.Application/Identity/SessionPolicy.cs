namespace Hsm.Application.Identity;

/// <summary>
/// How long a browser session lives, stated once. It is the cookie's
/// <c>ExpireTimeSpan</c> AND the horizon written into the session row, so the
/// two halves of a session — the cookie the browser holds and the row the
/// server holds — cannot drift into disagreeing about when it ended.
///
/// <para>"Cannot drift" is a claim about the SCHEDULE as much as the number,
/// which is why there is only one constant here now. The cookie renews on every
/// cookie-authenticated request (Identity's security-stamp validator sets
/// <c>ShouldRenew</c> whenever it revalidates, and
/// <c>SecurityStampValidatorOptions.ValidationInterval</c> is zero), so
/// <c>HsmSessionValidator</c> slides the row on exactly the same trigger:
/// every request, to <c>now + Lifetime</c>. An earlier version extended the row
/// only once fewer than four hours were left, which made the effective idle
/// timeout anything between four and eight hours — safe (a session ends early,
/// never late) but not what either this type or the cookie claimed. The price is
/// one indexed UPDATE per cookie-authenticated request, which is the same order
/// as the handful of reads the zero validation interval already costs; see
/// <c>AddHsmIdentityAuthentication</c>'s note on that trade.</para>
/// </summary>
public static class SessionPolicy
{
    /// <summary>
    /// Sliding: eight hours of INACTIVITY ends a session, roughly a shift. An
    /// active session is renewed rather than cut off mid-task.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
}
