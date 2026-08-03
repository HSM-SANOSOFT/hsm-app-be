using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Hsm.Infrastructure.Jobs;

/// <summary>
/// Moves due entries from each queue's delayed sorted set onto its due stream,
/// on a <see cref="PeriodicTimer"/>.
///
/// <para>Redis Streams have no native "deliver this later", so backoff and the
/// frozen first-attempt delay are stored as a score — the wall clock at which
/// the job becomes due — rather than slept through by whichever process
/// happened to observe the failure. That is what makes a retry survive the
/// crash of the consumer that scheduled it, which an in-process
/// <c>Task.Delay</c> could never do.</para>
///
/// <para>Promotion is claimed with ZREM: several pumps may see the same due
/// entry, but only the one whose ZREM returns 1 XADDs it, so a job cannot be
/// promoted twice. The reverse order (XADD then ZREM) would duplicate the job
/// on a crash in between; this order can at worst lose the promotion of an
/// entry whose XADD never ran, which XAUTOCLAIM cannot recover — accepted
/// because the window is a single round trip and the alternative duplicates
/// sends.</para>
/// </summary>
public sealed partial class DelayedJobPump(
    IJobConnection connection,
    JobQueueTopology topology,
    ILogger<DelayedJobPump> logger) : BackgroundService
{
    /// <summary>Promotes every due entry on <paramref name="queue"/>; returns how many moved.</summary>
    internal static async Task<int> PumpAsync(IDatabase db, JobQueueTopology topology, string queue)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var due = await db.SortedSetRangeByScoreAsync(
            topology.DelayedKey(queue),
            double.NegativeInfinity,
            now,
            take: 100).ConfigureAwait(false);

        var moved = 0;
        foreach (var member in due)
        {
            // ZREM is the claim: exactly one pump gets 1 back for a member.
            if (!await db.SortedSetRemoveAsync(topology.DelayedKey(queue), member).ConfigureAwait(false))
            {
                continue;
            }

            var delayed = DelayedJob.FromJson(member!);
            await RedisStreamJobQueue.AddAsync(
                db, topology, topology.DueKey(queue), delayed.Envelope).ConfigureAwait(false);
            moved++;
        }

        return moved;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(topology.DelayedPumpInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var db = await connection.GetDatabaseAsync().ConfigureAwait(false);
                foreach (var queue in topology.Queues)
                {
                    await PumpAsync(db, topology, queue.Name).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A Redis outage must not take the host down: the entries stay
                // due and the next tick promotes them.
                LogPumpFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delayed job pump tick failed; retrying on the next tick")]
    private static partial void LogPumpFailed(ILogger logger, Exception exception);
}
