using Hsm.Contracts;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>
/// The three things UserManager cannot do, and nothing else.
///
/// <para>It is named Directory rather than Store because
/// <c>Microsoft.AspNetCore.Identity.IUserStore&lt;TUser&gt;</c> is a real type
/// that is in scope in the same files from now on, and two IUserStores in one
/// using-block is a compile error nobody should have to debug twice.</para>
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Scalar onboarding probe for the request actor: whether a LIVE user row
    /// exists and, if so, its onboardingCompletedAt — without loading the
    /// aggregate. A deleted or deactivated account reports not-found, which is
    /// what makes the actor fail closed.
    /// </summary>
    Task<(bool Found, DateTimeOffset? OnboardingCompletedAt)> OnboardingStateAsync(
        Guid id, CancellationToken ct = default);

    /// <summary>Paged listing, newest first.</summary>
    Task<PagedResult<HsmUser>> ListAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// The roles held by each of these users, in ONE round trip.
    /// <see cref="Microsoft.AspNetCore.Identity.UserManager{TUser}.GetRolesAsync"/>
    /// is per-user, so a 20-row page would otherwise issue 21 queries.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> RolesForAsync(
        IReadOnlyList<Guid> userIds, CancellationToken ct = default);
}
