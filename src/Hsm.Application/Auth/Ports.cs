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

    /// <summary>Stages a new user with role rows resolved via the role catalog.</summary>
    Task AddAsync(User user, IEnumerable<string> roles, CancellationToken ct = default);

    Task UpdatePasswordAsync(Guid userId, string passwordHash, CancellationToken ct = default);

    /// <summary>Paged listing, newest first, roles attached (frozen findAll).</summary>
    Task<(IReadOnlyList<User> Users, int TotalItems)> ListAsync(int page, int limit, CancellationToken ct = default);

    /// <summary>
    /// Replaces the user's role rows: deletes the existing rows IMMEDIATELY
    /// (the caller supplies the surrounding transaction) and stages the
    /// replacements — the frozen delete-then-insert order, so re-assigning a
    /// held role cannot trip the (user, domain, role) unique index. Unknown
    /// roles resolve to no domain and are skipped, as in AddAsync.
    /// </summary>
    Task ReplaceRolesAsync(Guid userId, IEnumerable<string> roles, CancellationToken ct = default);

    /// <summary>Fresh, untracked role rows for a user.</summary>
    Task<IReadOnlyList<UserRole>> RolesOfAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>Refresh-token store for human users — never shared with integrations.</summary>
public interface IUserRefreshTokenStore
{
    Task<UserRefreshToken?> FindActiveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Deactivates all active rows for the user; returns rows affected.</summary>
    Task<int> DeactivateActiveAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(Guid userId, string tokenHash, CancellationToken ct = default);
}

/// <summary>Refresh-token store for integration accounts — never shared with users.</summary>
public interface IIntegrationRefreshTokenStore
{
    Task<IntegrationRefreshToken?> FindActiveAsync(Guid integrationAccountId, CancellationToken ct = default);

    Task<int> DeactivateActiveAsync(Guid integrationAccountId, CancellationToken ct = default);

    Task AddAsync(Guid integrationAccountId, string tokenHash, CancellationToken ct = default);
}

/// <summary>Persistence port for integration accounts.</summary>
public interface IIntegrationAccountStore
{
    Task AddAsync(IntegrationAccount account, CancellationToken ct = default);
}

/// <summary>Persistence port for password-reset tokens.</summary>
public interface IPasswordResetTokenStore
{
    Task<int> CountForUserSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    Task AddAsync(PasswordResetToken token, CancellationToken ct = default);

    Task<PasswordResetToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Stamps usedAt conditionally on the row still being unused — the atomic
    /// single-use enforcement. Returns false when a racing request already
    /// consumed it.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid tokenId, CancellationToken ct = default);

    Task DeleteByHashAsync(string tokenHash, CancellationToken ct = default);
}

/// <summary>Commits staged work; groups multi-store writes atomically.</summary>
public interface IAuthUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Runs the work inside a single database transaction.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}

/// <summary>Bcrypt in the adapter; the application sees only hash/verify.</summary>
public interface IPasswordHasher
{
    string Hash(string plaintext);
    bool Verify(string plaintext, string hash);
}

/// <summary>Outcome of validating a JWT: the principal, or why not.</summary>
public sealed record TokenValidation(AuthPrincipal? Principal, bool IsExpired)
{
    public static readonly TokenValidation Invalid = new(null, IsExpired: false);
    public static readonly TokenValidation Expired = new(null, IsExpired: true);
}

/// <summary>
/// Signs and validates the two JWT families (access vs refresh secret).
/// Claim layout is the adapter's business; the application deals in
/// <see cref="AuthPrincipal"/>.
/// </summary>
public interface IAuthTokenCodec
{
    string Sign(AuthPrincipal principal, TokenKind kind, TimeSpan lifetime);

    /// <summary>
    /// Validates signature (and expiry unless ignored) against the secret for
    /// <paramref name="kind"/>.
    /// </summary>
    Task<TokenValidation> ValidateAsync(string token, TokenKind kind, bool ignoreExpiration = false);
}

/// <summary>
/// Outbound recovery email delivery. The frozen system enqueued to the coms
/// queue; delivery transport arrives with the communications module — this
/// port isolates auth from that timing.
/// </summary>
public interface IRecoveryEmailer
{
    /// <summary>
    /// Delivers the reset link for <paramref name="resetToken"/>. The token
    /// must ride in the link's URL fragment and never be persisted or logged.
    /// </summary>
    Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default);

    Task SendUsernameReminderAsync(string toEmail, string username, CancellationToken ct = default);
}

/// <summary>
/// Deployment environment gate for the developer role (frozen envs.ENVIRONMENT):
/// developer tokens/access are only permitted in dev.
/// </summary>
public interface IEnvironmentPolicy
{
    bool IsDev { get; }
}
