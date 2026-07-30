using Hsm.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Persistence;

/// <summary>
/// The pipeline's <see cref="IUnitOfWork"/>, wrapping the same execution-strategy
/// pattern as <see cref="Hsm.Infrastructure.Identity.AuthUnitOfWork"/>: Npgsql's
/// retrying execution strategy must own the transaction, so BeginTransactionAsync
/// runs inside ExecuteAsync rather than around it.
///
/// <para><b>Joins an ambient transaction rather than nesting.</b> When this
/// DbContext already has an open transaction — which it does for every ICommand,
/// because TransactionBehavior opens one — the work runs inside that transaction
/// and commits or rolls back with it, instead of asking EF for a nested
/// transaction it refuses outright. A caller that needs an INDEPENDENT
/// transaction (an audit row, an outbox write, or a compensating delete that must
/// survive an outer rollback) must NOT use this type: it will be silently
/// absorbed into the outer unit. The join is logged at Debug so the case is
/// discoverable instead of invisible.</para>
/// </summary>
public sealed partial class EfUnitOfWork(HsmDbContext db, ILogger<EfUnitOfWork> logger) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
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
        Message = "EfUnitOfWork.ExecuteInTransactionAsync joined the ambient transaction {TransactionId} "
            + "instead of opening its own; this work commits or rolls back with the outer unit")]
    private static partial void LogJoinedAmbientTransaction(ILogger logger, Guid transactionId);
}
