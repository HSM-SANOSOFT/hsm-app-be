using Hsm.Application.Abstractions;

namespace Hsm.Application.Ports;

/// <summary>
/// The background-work hand-off. One port for every module: a queued job IS a
/// command, carrying the same policy attributes and running through the same
/// pipeline as its HTTP counterpart — the queue only decides WHERE and WHEN it
/// runs, never what it is allowed to do.
///
/// <para>The enqueuing actor is captured by the adapter from
/// <see cref="ICurrentPrincipal"/> at the moment of the call and travels with
/// the job, so <c>AuthorizationBehavior</c> sees the same actor in the worker
/// that the edge saw. Callers therefore never pass an identity.</para>
///
/// <para><b>Enqueue after the commit, not inside it.</b> The consumer runs in a
/// different process against a different connection: a job enqueued before its
/// own transaction commits can be picked up before the rows it names exist.
/// Handlers that persist state hand the enqueue to their caller (the endpoint),
/// which runs strictly after <c>IDispatcher.Send</c> returns.</para>
/// </summary>
// CA1711: the analyzer reserves the 'Queue' suffix for System.Collections
// types. This port is a queue in the only sense the word has here, it is the
// plan's fixed name for it, and every call site in the system reads better for
// saying so.
#pragma warning disable CA1711
public interface IJobQueue
#pragma warning restore CA1711
{
    /// <summary>Enqueues <paramref name="command"/> and returns its job id.</summary>
    Task<string> EnqueueAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand<Unit>;
}
