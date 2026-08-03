namespace Hsm.Application.Abstractions;

/// <summary>Commits a unit of work atomically inside a single database transaction.</summary>
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}
