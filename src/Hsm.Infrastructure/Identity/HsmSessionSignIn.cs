using System.Security.Claims;
using Hsm.Application.Identity;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// The ONE way a session is opened, renewed or closed, on either door.
///
/// <para>It exists because signing in is no longer a single call. A session now
/// has two halves — the cookie the browser gets and the row that says the
/// cookie is still good — and they have to be written together or the feature
/// silently degrades: mint a cookie without a row and the very next request is
/// a 401; delete a row without the cookie and the user is signed out of a
/// session they are still holding. Every call site that used to say
/// <c>SignInManager.SignInAsync</c> says <see cref="SignInAsync"/> here
/// instead, so neither door can get half of it right.</para>
///
/// <para>It lives in Hsm.Infrastructure next to the cookie configuration it
/// belongs to, and both hosts reach it through
/// <c>AddHsmIdentityAuthentication</c>.</para>
/// </summary>
public sealed class HsmSessionSignIn(
    SignInManager<HsmUser> signInManager,
    IUserSessionStore sessions,
    IHttpContextAccessor httpContextAccessor)
{
    /// <summary>
    /// Opens a NEW session: a fresh id every time, so two sign-ins by the same
    /// person are two independently revocable sessions rather than one shared
    /// one. That is the whole reason signing out on a shared workstation does
    /// not sign the same person out on the ward's other machine.
    ///
    /// <para><c>SignInAsync</c>, deliberately not <c>PasswordSignInAsync</c>:
    /// the latter re-resolves the account through the unfiltered
    /// <c>UserManager.FindByNameAsync</c> and can land on a soft-deleted row
    /// that shares the username with the live account the caller just
    /// authenticated.</para>
    /// </summary>
    public async Task SignInAsync(HsmUser user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var sessionId = Guid.NewGuid();
        await sessions.OpenAsync(sessionId, user.Id, DateTimeOffset.UtcNow + SessionPolicy.Lifetime, ct);

        // The row goes in FIRST. If the cookie were written first and the
        // insert then failed, the caller would hold a cookie that authenticates
        // nothing — a confusing 401 loop. This way the only failure is a
        // reclaimable orphan row and a sign-in that reports failing.
        await signInManager.SignInWithClaimsAsync(
            user, isPersistent: false, [new Claim(HsmClaims.SessionId, sessionId.ToString())]);
    }

    /// <summary>
    /// Reissues the CURRENT session's cookie, keeping its id.
    ///
    /// <para>Needed wherever an operation rotates the security stamp — setting
    /// a password during onboarding, changing your own password — because the
    /// stamp bump invalidates every cookie for the account including the
    /// caller's own. <c>SignInManager.RefreshSignInAsync</c> cannot be used
    /// directly: it rebuilds the principal from the claims factory and carries
    /// only <c>amr</c> across, so the session claim would be dropped and the
    /// caller's next request refused for having no session at all.</para>
    ///
    /// <para>No-op for a caller with no session claim — an integration
    /// authenticated by bearer must never be handed a cookie as a side
    /// effect.</para>
    /// </summary>
    public async Task RefreshAsync(HsmUser user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (CurrentSessionId() is not { } sessionId)
        {
            return;
        }

        await sessions.ExtendAsync(sessionId, DateTimeOffset.UtcNow + SessionPolicy.Lifetime, ct);
        await signInManager.SignInWithClaimsAsync(
            user, isPersistent: false, [new Claim(HsmClaims.SessionId, sessionId.ToString())]);
    }

    /// <summary>
    /// Closes THIS session: deletes the row, then hands the cookie back.
    ///
    /// <para>The delete is what makes sign-out real. Without it the cookie is
    /// merely asked to leave the browser, and any copy of it taken beforehand —
    /// from a shared workstation, a proxy log, malware — keeps authenticating
    /// for the rest of the sliding window. With it, the next request presenting
    /// that copy finds no row and is refused.</para>
    ///
    /// <para>Sign-out never fails for want of a session: a caller with no
    /// session claim, or one whose row is already gone, still gets its cookie
    /// cleared. Refusing would strand a browser holding a stale cookie, which
    /// is the one state this operation exists to fix.</para>
    /// </summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        if (CurrentSessionId() is { } sessionId)
        {
            await sessions.CloseAsync(sessionId, ct);
        }

        await signInManager.SignOutAsync();
    }

    private Guid? CurrentSessionId() =>
        Guid.TryParse(
            httpContextAccessor.HttpContext?.User.FindFirstValue(HsmClaims.SessionId), out var id)
                ? id
                : null;
}
