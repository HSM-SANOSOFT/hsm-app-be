using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>
/// Refresh-token store for integration accounts — never shared with users, who
/// have none.
///
/// <para>The row holds a DIGEST, so no lookup here can reproduce a credential.
/// <see cref="ClaimActiveAsync"/> is the one that matters: it is both the read
/// and the write of a rotation, which is what makes two concurrent redemptions
/// of the same token resolve to one winner rather than two.</para>
/// </summary>
public interface IIntegrationRefreshTokenStore
{
    Task<IntegrationRefreshToken?> FindActiveAsync(Guid integrationAccountId, CancellationToken ct = default);

    /// <summary>
    /// The account whose ACTIVE row carries <paramref name="tokenHash"/>, or
    /// null. Answers "which account is this?" for a token that says nothing
    /// about itself; it does not claim the row.
    /// </summary>
    Task<Guid?> FindActiveAccountByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// The account whose row carries <paramref name="tokenHash"/> whether or not
    /// it is still active, or null if this digest was never issued.
    ///
    /// <para>The distinction it draws is the whole point: a digest that was
    /// never issued is a guess, and a digest that was issued and SPENT is a
    /// replay — someone is holding a copy of a credential the account has moved
    /// past. Spent rows are kept rather than deleted precisely so this question
    /// can be answered.</para>
    /// </summary>
    Task<Guid?> FindAccountByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// The account's most recently spent row — the digest its LAST rotation
    /// retired — or null if it has never rotated.
    ///
    /// <para>Ordered by <c>UpdatedAt</c>, which is stamped at the moment a row
    /// is deactivated. That is what makes "was this spent just now?" answerable
    /// without a clock seam or a new column.</para>
    /// </summary>
    Task<IntegrationRefreshToken?> FindMostRecentlySpentAsync(
        Guid integrationAccountId, CancellationToken ct = default);

    /// <summary>
    /// Deactivates the active row carrying <paramref name="tokenHash"/> and
    /// returns how many rows that was — 1 for the caller that won the token, 0
    /// for anyone presenting it afterwards, including a concurrent redemption
    /// that lost the race for the same row.
    /// </summary>
    Task<int> ClaimActiveAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Deactivates every row active for the account and returns how many that
    /// was — INCLUDING one a concurrent rotation committed while this call was
    /// blocked behind its lock. Returning 0 therefore means the account really
    /// has no live credential, not merely that this caller lost a race.
    ///
    /// <para>That guarantee is the adapter's to keep, and it is not free: see
    /// the implementation for why a single statement cannot provide it under
    /// READ COMMITTED. Callers rely on it to distinguish "already revoked" from
    /// "revoked something", so it is stated here rather than left to whoever
    /// reads the SQL.</para>
    /// </summary>
    Task<int> DeactivateActiveAsync(Guid integrationAccountId, CancellationToken ct = default);

    Task AddAsync(Guid integrationAccountId, string tokenHash, CancellationToken ct = default);
}
