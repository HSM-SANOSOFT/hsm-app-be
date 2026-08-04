using Hsm.Contracts;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>Persistence port for human users and their role rows.</summary>
public interface IUserStore
{
    /// <summary>Case-insensitive lookup, roles attached; null when absent.</summary>
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default);

    Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Active (isActive) account owning the email, or null.</summary>
    Task<User?> FindActiveByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Scalar onboarding probe for the request guard: whether a live user row
    /// exists and, if so, its onboardingCompletedAt — without loading the
    /// aggregate.
    /// </summary>
    Task<(bool Found, DateTimeOffset? OnboardingCompletedAt)> OnboardingStateAsync(
        Guid id, CancellationToken ct = default);

    /// <summary>Stages a new user with role rows resolved via the role catalog.</summary>
    Task AddAsync(User user, IEnumerable<string> roles, CancellationToken ct = default);

    Task UpdatePasswordAsync(Guid userId, string passwordHash, CancellationToken ct = default);

    /// <summary>Paged listing, newest first, roles attached (frozen findAll).</summary>
    Task<PagedResult<User>> ListAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Replaces the user's role rows: deletes the existing rows IMMEDIATELY
    /// (the caller supplies the surrounding transaction) and stages the
    /// replacements — the frozen delete-then-insert order, so re-assigning a
    /// held role cannot trip the (user, domain, role) unique index. Unknown
    /// roles resolve to no domain and are skipped, as in AddAsync. Returns
    /// the staged replacement rows.
    /// </summary>
    Task<IReadOnlyList<UserRole>> ReplaceRolesAsync(
        Guid userId, IEnumerable<string> roles, CancellationToken ct = default);
}
