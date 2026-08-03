using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>Persistence port for integration accounts.</summary>
public interface IIntegrationAccountStore
{
    Task AddAsync(IntegrationAccount account, CancellationToken ct = default);

    Task<IntegrationAccount?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Live (non-deleted) accounts, newest first.</summary>
    Task<IReadOnlyList<IntegrationAccount>> ListAsync(CancellationToken ct = default);
}
