namespace Hsm.Application.Abstractions;

public interface IRequestHandler<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(TRequest request, CancellationToken ct);
}

// CA1711: "Delegate" suffix is the CQRS plan's fixed name for this type —
// every later task's pipeline behaviors depend on this exact identifier.
#pragma warning disable CA1711
/// <summary>Invokes the next stage of the pipeline (or the handler itself).</summary>
public delegate Task<TResult> RequestHandlerDelegate<TResult>();
#pragma warning restore CA1711

/// <summary>
/// A cross-cutting stage. Registered order is outermost-first; see the plan's
/// behavior-order decision (telemetry, authorization, validation, transaction).
/// </summary>
public interface IPipelineBehavior<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    // CA1716: "next" is the plan's fixed parameter name for this port; renaming
    // it would break the exact signature every later behavior implements against.
#pragma warning disable CA1716
    Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct);
#pragma warning restore CA1716
}
