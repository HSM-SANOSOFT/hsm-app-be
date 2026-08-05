using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Identity;

/// <summary>EF Core adapter for the INTEGRATION refresh-token store.</summary>
public sealed class IntegrationRefreshTokenStore(HsmDbContext db) : IIntegrationRefreshTokenStore
{
    public Task<IntegrationRefreshToken?> FindActiveAsync(Guid integrationAccountId, CancellationToken ct = default) =>
        db.IntegrationRefreshTokens
            .FirstOrDefaultAsync(t => t.IntegrationAccountId == integrationAccountId && t.IsActive, ct);

    public Task<int> DeactivateActiveAsync(Guid integrationAccountId, CancellationToken ct = default) =>
        db.IntegrationRefreshTokens
            .Where(t => t.IntegrationAccountId == integrationAccountId && t.IsActive)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsActive, false)
                    .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

    public async Task AddAsync(Guid integrationAccountId, string tokenHash, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.IntegrationRefreshTokens.AddAsync(
            new IntegrationRefreshToken
            {
                Id = Guid.NewGuid(),
                IntegrationAccountId = integrationAccountId,
                TokenHash = tokenHash,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            ct);
    }
}

/// <summary>EF Core adapter for <see cref="IIntegrationAccountStore"/>.</summary>
public sealed class IntegrationAccountStore(HsmDbContext db) : IIntegrationAccountStore
{
    public async Task AddAsync(IntegrationAccount account, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        account.CreatedAt = now;
        account.UpdatedAt = now;
        await db.IntegrationAccounts.AddAsync(account, ct);
    }

    public Task<IntegrationAccount?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.IntegrationAccounts.FirstOrDefaultAsync(a => a.Id == id && a.DeletedAt == null, ct);

    public async Task<IReadOnlyList<IntegrationAccount>> ListAsync(CancellationToken ct = default) =>
        await db.IntegrationAccounts
            .Where(a => a.DeletedAt == null)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
}

/// <summary>
/// Commits the shared DbContext; transactions span all stores in scope.
///
/// <para><b>Joins an ambient transaction rather than nesting.</b> When this
/// DbContext already has an open transaction — which it does for every ICommand,
/// because TransactionBehavior opens one — the work runs inside that transaction
/// and commits or rolls back with it, instead of asking EF for a nested
/// transaction it refuses outright. That is what lets shared collaborators below
/// the pipeline keep their own <c>ExecuteInTransactionAsync</c> calls, TokenIssuer's
/// refresh rotation first among them. A caller that needs an INDEPENDENT
/// transaction (an audit row, an outbox write, or a compensating delete that must
/// survive an outer rollback) must NOT use this type: it will be silently
/// absorbed into the outer unit. The join is logged at Debug so the case is
/// discoverable instead of invisible.</para>
/// </summary>
public sealed partial class AuthUnitOfWork(HsmDbContext db, ILogger<AuthUnitOfWork> logger) : IAuthUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        var ambient = db.Database.CurrentTransaction;
        if (ambient is not null)
        {
            LogJoinedAmbientTransaction(logger, ambient.TransactionId);
            return await work(ct);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "AuthUnitOfWork.ExecuteInTransactionAsync joined the ambient transaction {TransactionId} "
            + "instead of opening its own; this work commits or rolls back with the outer unit")]
    private static partial void LogJoinedAmbientTransaction(ILogger logger, Guid transactionId);
}
