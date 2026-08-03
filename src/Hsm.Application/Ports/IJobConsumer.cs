namespace Hsm.Application.Ports;

/// <summary>
/// The consuming half of <see cref="IJobQueue"/> — the worker's entry point.
/// One call reads at most <paramref name="max"/> jobs, runs each through the
/// dispatcher pipeline, and settles it (acknowledged, rescheduled for retry, or
/// dead-lettered). A host's consume loop is a <c>while</c> around this method;
/// tests call it directly, which is why the unit of work is "drain once" rather
/// than "run forever".
/// </summary>
public interface IJobConsumer
{
    /// <summary>Drains up to <paramref name="max"/> jobs from <paramref name="queue"/>;
    /// returns how many were processed.</summary>
    Task<int> DrainOnceAsync(string queue, int max, CancellationToken ct);
}
