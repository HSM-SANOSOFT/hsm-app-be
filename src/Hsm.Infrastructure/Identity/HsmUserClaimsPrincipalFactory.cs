using System.Globalization;
using System.Security.Claims;
using Hsm.Application.Identity;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// Adds the onboarding timestamp to the principal so the common case — an
/// onboarded user — needs no database round trip per request. It is a CACHE:
/// <see cref="RequestActorFactory"/> falls back to the user row whenever the
/// claim is absent or empty, which is what makes a deleted or still-pending
/// account fail closed even while it holds a valid cookie.
/// </summary>
public sealed class HsmUserClaimsPrincipalFactory(
    UserManager<HsmUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<HsmUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(HsmUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = await base.GenerateClaimsAsync(user);
        if (user.OnboardingCompletedAt is { } completedAt)
        {
            identity.AddClaim(new Claim(
                HsmClaims.OnboardingCompletedAt,
                completedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        }

        return identity;
    }
}
