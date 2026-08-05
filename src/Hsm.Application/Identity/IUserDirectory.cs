using Hsm.Contracts;
using Hsm.Domain.Identity;

namespace Hsm.Application.Identity;

/// <summary>
/// The things UserManager cannot do, and nothing else.
///
/// <para>It is named Directory rather than Store because ASP.NET Core
/// Identity's own user-store abstraction is a real type in scope in the same
/// files, and two of them in one using-block is a compile error nobody should
/// have to debug twice.</para>
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// The LIVE account with this login name, or null.
    ///
    /// <para>UserManager.FindByNameAsync cannot be used for this.
    /// It is an unfiltered FirstOrDefault on the normalized column, and the
    /// unique indexes on <c>users</c> are filtered on DeletedAt precisely so a
    /// soft-deleted account frees its name for reuse — so a soft-deleted row
    /// and a live row may legitimately share one, and the unfiltered lookup can
    /// return either. Anything that goes on to treat the result as "the account"
    /// has to ask for the live one explicitly.</para>
    /// </summary>
    Task<HsmUser?> FindLiveByNameAsync(string username, CancellationToken ct = default);

    /// <summary>The LIVE account with this email, or null — see <see cref="FindLiveByNameAsync"/>.</summary>
    Task<HsmUser?> FindLiveByEmailAsync(string email, CancellationToken ct = default);

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
