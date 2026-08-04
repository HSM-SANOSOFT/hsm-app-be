namespace Hsm.Application.Auth;

/// <summary>
/// Claim types this system mints itself, on top of the ones ASP.NET Core
/// Identity and the JWT layout already define.
///
/// <para><b>Why here and not in Hsm.Infrastructure.</b> The claim is written by
/// Infrastructure (<c>HsmUserClaimsPrincipalFactory</c>) and read by
/// Application (<see cref="RequestActorFactory"/>). The dependency arrow runs
/// Infrastructure → Application, so the shared name has to sit on the
/// Application side or the reader could not name it.</para>
/// </summary>
public static class HsmClaims
{
    /// <summary>
    /// ISO-8601 onboarding completion, cached in the principal. The USER ROW
    /// remains authoritative — see <see cref="RequestActorFactory"/> for why
    /// this is a cache and not the truth.
    /// </summary>
    public const string OnboardingCompletedAt = "hsm:onboarding_completed_at";
}
