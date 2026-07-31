using System.Security.Claims;
using Hsm.Application.Auth;

namespace Hsm.Web.Auth;

/// <summary>
/// Bridges the application-layer principal (validated by <see cref="RequestAuth"/>
/// machinery) into the ClaimsPrincipal ASP.NET Core authentication and Blazor
/// authorization consume.
/// </summary>
public static class AuthPrincipalClaims
{
    /// <summary>Authentication type carried by shell sessions.</summary>
    public const string AuthenticationType = "HsmCookie";

    /// <summary>
    /// The token's onboardingCompletedAt, carried onto the shell principal so
    /// <see cref="ShellActor"/> can derive onboarding state the same way the
    /// REST edge does. Emitted ONLY when the principal is actually onboarded:
    /// the frozen guard treated an absent claim and a null claim identically
    /// (both defer to the user row), so absence is the faithful encoding of
    /// "pending or unknown" rather than a lost signal.
    /// </summary>
    public const string OnboardingCompletedAtClaim = "hsm:onboarding_completed_at";

    public static ClaimsPrincipal ToClaimsPrincipal(this AuthPrincipal principal)
    {
        var identity = new ClaimsIdentity(AuthenticationType, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, principal.Id));

        var displayName = principal.Username ?? principal.Name ?? principal.Email;
        if (displayName is not null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, displayName));
        }

        foreach (var role in principal.Roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        if (!string.IsNullOrEmpty(principal.OnboardingCompletedAt))
        {
            identity.AddClaim(new Claim(OnboardingCompletedAtClaim, principal.OnboardingCompletedAt));
        }

        return new ClaimsPrincipal(identity);
    }
}
