using System.Reflection;

namespace Hsm.Application.Abstractions.Behaviors;

/// <summary>
/// Innermost behavior. Commands run in a transaction; queries never do —
/// checked at runtime rather than by registering a closed generic, because the
/// dispatcher resolves behaviors for IRequest and both kinds share the chain.
///
/// A command marked <see cref="NoAmbientTransactionAttribute"/> owns its own
/// commits and is passed straight through; see that attribute for the one case
/// this system has — a queued job whose contract is to persist what went wrong
/// and then throw.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResult>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    /// <summary>Per closed generic, so the reflection happens once per request type.</summary>
    private static readonly bool OwnsItsCommits =
        typeof(TRequest).GetCustomAttribute<NoAmbientTransactionAttribute>() is not null;

    public Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
        => request is ICommand<TResult> && !OwnsItsCommits
            ? unitOfWork.ExecuteInTransactionAsync(_ => next(), ct)
            : next();
}
