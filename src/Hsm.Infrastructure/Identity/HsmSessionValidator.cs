using System.Security.Claims;
using Hsm.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// The read half of per-session revocation: on every cookie-authenticated
/// request, the session the cookie names must still be open.
///
/// <para>It runs AFTER the security-stamp validator, chained onto the same
/// <c>OnValidatePrincipal</c> event, and the two answer different questions.
/// The stamp asks "is this ACCOUNT still in the state the cookie was issued
/// for?" — a password or role change bumps it and every session for that user
/// dies at once. This asks "is THIS session still open?" — one sign-out, one
/// session. Neither subsumes the other, which is why both run.</para>
///
/// <para>It fails CLOSED. A cookie with no session claim at all is refused, not
/// waved through: the only cookies without one are pre-feature relics and
/// anything hand-assembled, and "authenticate as if sessions did not exist" is
/// the one answer that would quietly disable the whole mechanism.</para>
/// </summary>
public static class HsmSessionValidator
{
    /// <summary>
    /// Chains the session check onto whatever <c>OnValidatePrincipal</c> is
    /// already configured — Identity's security-stamp validator — rather than
    /// replacing it. Replacing it would disable stamp-based revocation
    /// silently, and the tests that cover it would keep passing right up until
    /// a demoted user kept their old role.
    /// </summary>
    public static void ChainOnto(CookieAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var validateStamp = options.Events.OnValidatePrincipal;
        options.Events.OnValidatePrincipal = async context =>
        {
            await validateStamp(context);

            // The stamp check rejects by nulling the principal. Nothing left to
            // ask about — and re-answering would only risk turning its refusal
            // into a different one.
            if (context.Principal is null)
            {
                return;
            }

            await ValidateAsync(context);
        };
    }

    private static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var sessions = context.HttpContext.RequestServices.GetRequiredService<IUserSessionStore>();
        var ct = context.HttpContext.RequestAborted;

        if (!Guid.TryParse(context.Principal!.FindFirstValue(HsmClaims.SessionId), out var sessionId))
        {
            await RejectAsync(context);
            return;
        }

        var expiresAt = await sessions.ExpiresAtAsync(sessionId, ct);
        var now = DateTimeOffset.UtcNow;
        if (expiresAt is null || expiresAt <= now)
        {
            // Revoked by a sign-out, or reclaimed after expiry. Both mean the
            // cookie in hand is worthless, whatever its own expiry says.
            await RejectAsync(context);
            return;
        }

        // Keep the row ahead of the sliding cookie, and only when it has fallen
        // into the last half of its window — recomputing it every request would
        // be a database WRITE on every request. See SessionPolicy.
        if (expiresAt.Value - now < SessionPolicy.ExtendWhenRemainingBelow)
        {
            await sessions.ExtendAsync(sessionId, now + SessionPolicy.Lifetime, ct);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();

        // Take the cookie back as well as refusing it. Without this the browser
        // keeps replaying a credential that can never work again, and every
        // subsequent request pays the lookup above to say no a second time.
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}
