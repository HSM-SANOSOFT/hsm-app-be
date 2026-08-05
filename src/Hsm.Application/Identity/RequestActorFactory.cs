using System.Security.Claims;
using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Identity;

/// <summary>
/// Builds the <see cref="RequestActor"/> the application pipeline authorizes
/// against. This is the one place the onboarding-guard derivation lives,
/// expressed as state rather than as an edge refusal:
///
/// <list type="number">
/// <item>integration and admin principals are exempt outright, so they are
/// recorded as onboarded regardless of what their row says;</item>
/// <item>a non-empty onboardingCompletedAt claim is trusted;</item>
/// <item>an absent OR null claim defers to the authoritative user row and
/// fails closed — a deleted account and a still-pending account both produce
/// a pending actor, which every request that is not
/// <c>[AllowPendingOnboarding]</c> then refuses in the pipeline.</item>
/// </list>
///
/// Point 3 is why this is async and why the derivation cannot live inside
/// <see cref="ICurrentPrincipal"/>'s synchronous property: the claim is a
/// cache, the row is the truth, and this factory reads the row whenever the
/// cache says "pending".
///
/// <para><b>Why it lives in Hsm.Application rather than in a host.</b> Every
/// edge that publishes an actor calls it — the REST door's token path
/// (<c>Hsm.Api.Identity.RequestAuth</c>), the shell's claims path
/// (<c>Hsm.Web.Auth.ShellActor</c>), and the worker's job envelope next. It
/// touches no transport type: its inputs are an id, a role list and a claim
/// string, and its only dependency is <see cref="IUserDirectory"/>. A copy per
/// host would mean maintaining the onboarding rule in three places, and a
/// copy that drifts is a silent authorization divergence between doors — the
/// exact failure Task 15 spent itself removing.</para>
/// </summary>
public sealed class RequestActorFactory(IUserDirectory users)
{
    /// <param name="onboardingCompletedAt">
    /// The principal's onboardingCompletedAt claim, or <see langword="null"/>
    /// when the claim is absent. Absent and null are treated identically —
    /// both fall through to the database.
    /// </param>
    public async Task<RequestActor> CreateAsync(
        string id,
        IReadOnlyList<string> roles,
        string? onboardingCompletedAt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return new RequestActor(
            id, roles, await OnboardingCompletedAsync(id, roles, onboardingCompletedAt, ct));
    }

    /// <summary>
    /// Builds the actor from an authenticated principal. Null for an
    /// unauthenticated one — the pipeline answers that with 401, so a route
    /// that needed no authentication is unaffected and one that did fails
    /// closed.
    ///
    /// <para>Roles are read through each identity's own
    /// <see cref="ClaimsIdentity.RoleClaimType"/> rather than through a fixed
    /// <see cref="ClaimTypes.Role"/>, because the two handlers behind the
    /// adaptive scheme genuinely disagree: the Identity cookie carries
    /// <see cref="ClaimTypes.Role"/>, an integration's JWT carries the
    /// <c>roles</c> claim. Reading the type the identity itself declares is
    /// what <see cref="ClaimsPrincipal.IsInRole"/> does, and it keeps this
    /// method from having to know which door the caller came through.</para>
    /// </summary>
    public async Task<RequestActor?> CreateAsync(
        ClaimsPrincipal principal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var roles = principal.Identities
            .SelectMany(identity => identity.FindAll(identity.RoleClaimType))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return await CreateAsync(
            id,
            roles,
            // The cookie's cached claim; a JWT carries the
            // onboardingCompletedAt name instead, and its absence here simply
            // routes that caller to the authoritative user row below.
            principal.FindFirstValue(HsmClaims.OnboardingCompletedAt),
            ct);
    }

    private async Task<bool> OnboardingCompletedAsync(
        string id, IReadOnlyList<string> roles, string? onboardingCompletedAt, CancellationToken ct)
    {
        if (roles.Contains(Roles.Integration) || roles.Contains(Roles.Admin))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(onboardingCompletedAt))
        {
            return true;
        }

        if (!Guid.TryParse(id, out var userId))
        {
            // Only integration subjects carry a non-GUID id, and they were
            // exempted above; anything else here is malformed, so fail closed.
            return false;
        }

        var (found, completedAt) = await users.OnboardingStateAsync(userId, ct);
        return found && completedAt is not null;
    }
}
