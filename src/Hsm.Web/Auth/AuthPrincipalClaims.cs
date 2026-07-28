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

        return new ClaimsPrincipal(identity);
    }
}
