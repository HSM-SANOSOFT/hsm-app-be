namespace Hsm.Application.Abstractions.Behaviors;

/// <summary>
/// Innermost behavior. Commands run in a transaction; queries never do —
/// checked at runtime rather than by registering a closed generic, because the
/// dispatcher resolves behaviors for IRequest and both kinds share the chain.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResult>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
        => request is ICommand<TResult>
            ? unitOfWork.ExecuteInTransactionAsync(_ => next(), ct)
            : next();
}
