using Hsm.Application.Abstractions;

namespace Hsm.Worker.Scheduling;

/// <summary>
/// One recurring piece of work: a name, how often it runs, and what it
/// dispatches. <paramref name="Run"/> takes the dispatcher rather than a
/// command so a schedule can be a query-then-command sequence when it needs to
/// be — but it still goes through the pipeline, so a scheduled command is
/// authorized, validated and traced like every other.
/// </summary>
/// <param name="Name">Stable and unique: it is the Redis key the tick is claimed under.</param>
/// <param name="Interval">Time between ticks.</param>
/// <param name="Run">What one tick does.</param>
public sealed record ScheduledJob(
    string Name, TimeSpan Interval, Func<IDispatcher, CancellationToken, Task> Run);

/// <summary>
/// The one place recurring work is declared. It is EMPTY, and that is the
/// current state of the system, not an oversight: the frozen application had no
/// cron — every background job was enqueued by a request — so there is nothing
/// to carry over. It exists because the alternative to one declaration point is
/// a <c>PeriodicTimer</c> hidden in whichever service first needed one.
///
/// <para><b>Out of scope, deliberately:</b> this is interval scheduling, not
/// durable exactly-once scheduling. A tick is claimed by whichever worker gets
/// there first (see <see cref="Scheduler"/>), and that is all: a tick that
/// arrives while every worker is down is skipped rather than caught up, a
/// worker that dies mid-run does not hand the run to anyone, and intervals are
/// measured from process start rather than against a calendar. Anything that
/// needs "this ran exactly once for the 03:00 window, and we can prove it"
/// needs a persisted schedule table with per-occurrence rows, which is a
/// feature, not a wiring change.</para>
/// </summary>
public sealed class ScheduleRegistry
{
    private readonly List<ScheduledJob> _jobs = [];

    public IReadOnlyList<ScheduledJob> Jobs => _jobs;

    /// <summary>Declares a recurring job. Names must be unique — the name is the lease key.</summary>
    public ScheduleRegistry Add(ScheduledJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.Name);
        if (job.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentException($"Schedule '{job.Name}' needs a positive interval.", nameof(job));
        }

        if (_jobs.Any(existing => string.Equals(existing.Name, job.Name, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"A schedule named '{job.Name}' is already registered; two would share one lease key.",
                nameof(job));
        }

        _jobs.Add(job);
        return this;
    }
}
