using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>Refresh-token store for integration accounts — never shared with users.</summary>
public interface IIntegrationRefreshTokenStore
{
    Task<IntegrationRefreshToken?> FindActiveAsync(Guid integrationAccountId, CancellationToken ct = default);

    Task<int> DeactivateActiveAsync(Guid integrationAccountId, CancellationToken ct = default);

    Task AddAsync(Guid integrationAccountId, string tokenHash, CancellationToken ct = default);
}
