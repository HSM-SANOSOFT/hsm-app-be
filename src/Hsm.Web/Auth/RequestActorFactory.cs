using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Domain.Identity;

namespace Hsm.Web.Auth;

/// <summary>
/// Builds the <see cref="RequestActor"/> the application pipeline authorizes
/// against. This is the ONE place the frozen OnboardingGuard's derivation
/// survives, now expressed as state rather than as an edge refusal:
///
/// <list type="number">
/// <item>integration and admin principals are exempt outright — the frozen
/// guard returned before looking at anything else, so they are recorded as
/// onboarded regardless of what their row says;</item>
/// <item>a non-empty onboardingCompletedAt claim is trusted;</item>
/// <item>an absent OR null claim defers to the authoritative user row and
/// fails closed — a deleted account and a still-pending account both produce
/// a pending actor, which every request that is not
/// <c>[AllowPendingOnboarding]</c> then refuses in the pipeline.</item>
/// </list>
///
/// Point 3 is why this is async and why the derivation cannot live inside
/// <see cref="ICurrentPrincipal"/>'s synchronous property: the claim is a
/// cache, the row is the truth, and the frozen system read the row whenever
/// the cache said "pending". Both hosts' edges (the REST token path in
/// <see cref="RequestAuth"/>, the shell claims path in <see cref="ShellActor"/>)
/// call this rather than deriving onboarding themselves.
/// </summary>
public sealed class RequestActorFactory(IUserStore users)
{
    /// <param name="onboardingCompletedAt">
    /// The principal's onboardingCompletedAt claim, or <see langword="null"/>
    /// when the claim is absent. The frozen guard treated absent and null
    /// identically — both fall through to the database.
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
