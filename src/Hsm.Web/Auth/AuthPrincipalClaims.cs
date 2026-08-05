using System.Security.Claims;
using Hsm.Application.Identity;

namespace Hsm.Web.Auth;

/// <summary>
/// Bridges the application-layer <see cref="AuthPrincipal"/> into the
/// ClaimsPrincipal ASP.NET Core authentication and Blazor authorization
/// consume.
///
/// <para>Since Task 12 no request path uses this: the session principal is
/// built by Identity's own claims factory from the user row. What is left is
/// the ability to MAKE a shell session principal without an HTTP round trip,
/// which the in-process shell suites need in order to prove that the pipeline
/// — and not an edge — is what refuses a wrong-role dispatch. It emits the
/// same claim types Identity does, so those suites exercise the real reader.</para>
/// </summary>
public static class AuthPrincipalClaims
{
    /// <summary>Authentication type carried by a hand-built shell session.</summary>
    public const string AuthenticationType = "HsmCookie";

    /// <summary>
    /// The onboarding cache claim, by the one name both doors read
    /// (<see cref="HsmClaims.OnboardingCompletedAt"/>). Emitted ONLY when the
    /// principal is actually onboarded: an absent claim and a null claim are
    /// treated identically (both defer to the user row), so absence is the
    /// faithful encoding of "pending or unknown" rather than a lost signal.
    /// </summary>
    public const string OnboardingCompletedAtClaim = HsmClaims.OnboardingCompletedAt;

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
