using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

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
