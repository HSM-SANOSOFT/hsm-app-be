using Hsm.Application.Abstractions;
using Hsm.Infrastructure.Queue;
using StackExchange.Redis;

namespace Hsm.Worker.Scheduling;

/// <summary>
/// Fires the <see cref="ScheduleRegistry"/>'s jobs on a
/// <see cref="PeriodicTimer"/> each, once per tick across the whole worker
/// fleet.
///
/// <para><b>How "once" is enforced.</b> Every replica's timer ticks; before
/// running, each tries <c>SET {prefix}:sched:{name} {instanceId} NX PX
/// {interval * 0.9}</c>. <c>NX</c> makes exactly one of them the winner, and
/// only the winner dispatches. The key's lifetime is just under the interval,
/// so it is always gone before the next tick — no cleanup, and a schedule
/// cannot deadlock itself by leaving a claim behind.</para>
///
/// <para><b>What that is not.</b> It is at-most-once per tick, not
/// exactly-once: if the winner dies mid-run nothing re-runs it, and a tick that
/// no worker is up for is skipped. See <see cref="ScheduleRegistry"/> for why
/// durable scheduling is out of scope here.</para>
///
/// <para>Each run gets its own DI scope and its own dispatch, with no actor
/// installed: scheduled work has no user behind it, so a command that a
/// schedule dispatches must be one the pipeline lets through anonymously
/// (<c>[AllowAnonymousRequest]</c>) or one dispatched with an explicit actor
/// the schedule sets. It is not a hole — the default is refusal.</para>
/// </summary>
public sealed partial class Scheduler(
    ScheduleRegistry registry,
    IJobConnection connection,
    JobQueueTopology topology,
    IServiceScopeFactory scopeFactory,
    ILogger<Scheduler> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(registry.Jobs.Select(job => RunAsync(job, stoppingToken)));

    private async Task RunAsync(ScheduledJob job, CancellationToken ct)
    {
        LogScheduled(logger, job.Name, job.Interval);
        using var timer = new PeriodicTimer(job.Interval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var db = await connection.GetDatabaseAsync().ConfigureAwait(false);
                var won = await db.StringSetAsync(
                    topology.ScheduleKey(job.Name),
                    topology.ConsumerName,
                    job.Interval * 0.9,
                    When.NotExists).ConfigureAwait(false);
                if (!won)
                {
                    continue;
                }

                using var scope = scopeFactory.CreateScope();
                await job.Run(
                    scope.ServiceProvider.GetRequiredService<IDispatcher>(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // One schedule failing must not stop it ticking, and must not
                // stop the others.
                LogScheduleFailed(logger, exception, job.Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Schedule {Name} every {Interval}")]
    private static partial void LogScheduled(ILogger logger, string name, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled job {Name} failed; it will tick again")]
    private static partial void LogScheduleFailed(ILogger logger, Exception exception, string name);
}
