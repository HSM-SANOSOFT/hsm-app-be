namespace Hsm.Application.Abstractions;

/// <summary>
/// The transaction boundary and the flush, in one interface.
///
/// <para>There used to be two: IAuthUnitOfWork, historically named, was the one
/// 21 handlers across Users, Templates, Coms, Settings and Identity actually
/// used, while IUnitOfWork had exactly one consumer — TransactionBehavior.
/// Two names for one HsmDbContext is a standing invitation to believe they are
/// two boundaries. They never were.</para>
///
/// <para>The join rule is unchanged: the implementation JOINS an already-open
/// ambient transaction rather than nesting one. A caller that needs an
/// independent transaction — an audit row or a compensating action that must
/// survive an outer rollback — must not go through here; it will be silently
/// absorbed.</para>
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);

    /// <summary>Persists staged changes without opening a boundary of its own.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
