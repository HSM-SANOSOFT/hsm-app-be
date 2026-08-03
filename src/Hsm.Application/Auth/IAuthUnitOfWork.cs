namespace Hsm.Application.Auth;

/// <summary>Commits staged work; groups multi-store writes atomically.</summary>
public interface IAuthUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Runs the work inside a single database transaction.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}
