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
    /// Deactivates the active row carrying <paramref name="tokenHash"/> and
    /// returns how many rows that was — 1 for the caller that won the token, 0
    /// for anyone presenting it afterwards, including a concurrent redemption
    /// that lost the race for the same row.
    /// </summary>
    Task<int> ClaimActiveAsync(string tokenHash, CancellationToken ct = default);

    Task<int> DeactivateActiveAsync(Guid integrationAccountId, CancellationToken ct = default);

    Task AddAsync(Guid integrationAccountId, string tokenHash, CancellationToken ct = default);
}
