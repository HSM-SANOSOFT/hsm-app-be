using Hsm.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Persistence;

/// <summary>
/// The pipeline's <see cref="IUnitOfWork"/>, wrapping the same execution-strategy
/// pattern as <see cref="Hsm.Infrastructure.Identity.AuthUnitOfWork"/>: Npgsql's
/// retrying execution strategy must own the transaction, so BeginTransactionAsync
/// runs inside ExecuteAsync rather than around it.
/// </summary>
public sealed class EfUnitOfWork(HsmDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            // Reentrancy, same rule as AuthUnitOfWork: join the open
            // transaction rather than asking EF for a nested one it will refuse.
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
}
