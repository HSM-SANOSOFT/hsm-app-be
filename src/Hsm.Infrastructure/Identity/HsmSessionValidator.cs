using System.Security.Claims;
using Hsm.Application.Identity;
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
///
/// <para>It is also where a human caller's ACCOUNT state is enforced on an
/// already-issued cookie. Identity's stamp validator resolves the row through
/// the unfiltered <c>UserManager.FindByIdAsync</c>, which has no query filter on
/// <c>DeletedAt</c> and no opinion at all about <c>IsActive</c> — so on its own
/// it would keep validating a soft-deleted or deactivated account's session for
/// the rest of the sliding window. <c>IUserSessionStore.TouchAsync</c> folds
/// both clauses into the session read that already happens here, so enforcing
/// them costs no extra round trip.</para>
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

        var principal = context.Principal!;
        if (!Guid.TryParse(principal.FindFirstValue(HsmClaims.SessionId), out var sessionId)
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await RejectAsync(context);
            return;
        }

        // Checks and slides in one statement. False means revoked by a
        // sign-out, reclaimed after expiry, owned by a different account, or
        // owned by an account that has since been deactivated or soft-deleted —
        // all of which mean the cookie in hand is worthless, whatever its own
        // expiry says.
        //
        // The extension is unconditional because the COOKIE's is: with
        // SecurityStampValidatorOptions.ValidationInterval at zero the stamp
        // validator sets ShouldRenew on every request, so the browser's copy
        // slides a full Lifetime forward every time. A row that only slid in the
        // last half of its window would end the session up to half a Lifetime
        // before the cookie says it ends. See SessionPolicy.
        if (!await sessions.TouchAsync(sessionId, userId, DateTimeOffset.UtcNow + SessionPolicy.Lifetime, ct))
        {
            await RejectAsync(context);
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
