using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>Refresh-token store for human users — never shared with integrations.</summary>
public interface IUserRefreshTokenStore
{
    Task<UserRefreshToken?> FindActiveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Deactivates all active rows for the user; returns rows affected.</summary>
    Task<int> DeactivateActiveAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(Guid userId, string tokenHash, CancellationToken ct = default);
}
