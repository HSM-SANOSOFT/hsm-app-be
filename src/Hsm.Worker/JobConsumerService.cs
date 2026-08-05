using Hsm.Application.Ports;
using Hsm.Infrastructure.Queue;

namespace Hsm.Worker;

/// <summary>
/// The consume loop, and the only one in the deployment: a <c>while</c> around
/// <see cref="IJobConsumer.DrainOnceAsync"/> per configured consumer.
///
/// <para><b>This host and no other.</b> <c>Hsm.Api</c> and <c>Hsm.Web</c>
/// register the queue so they can enqueue and nothing else; background work
/// leaves the request-serving hosts entirely. That is also what makes the queue
/// namespace shared — an API replica that restarts strands nothing, because
/// what it wrote is in the same keys this service reads.</para>
///
/// <para><b>Parallelism is per queue</b>
/// (<see cref="JobQueueDefinition.Consumers"/>): <c>docs</c> runs four loops
/// because generation jobs are independent and one job's backoff must not
/// serialize the queue, while <c>coms</c> runs one, because resend ordering
/// depends on no newer job starting while an earlier one is still working
/// through its attempts.</para>
///
/// <para><b>And for a serial queue, one loop in the CLUSTER.</b> Counting loops
/// in this process would mean nothing with two worker replicas running, so a
/// serial queue's loop only runs while this replica holds that queue's
/// <see cref="QueueLease"/>. The others sit on the lease, consuming nothing,
/// until the holder releases it (a clean shutdown) or stops renewing it (a
/// crash) — at which point one of them takes over, at most
/// <see cref="JobQueueTopology.LeaseTtl"/> later. Jobs are not lost in that
/// window; they wait in Redis.</para>
///
/// <para><b>A serial queue therefore drains ONE job at a time.</b> The lease is
/// taken or renewed at the top of the loop, so everything between one check and
/// the next runs on the strength of the last one: a ten-job batch of slow sends
/// would sit inside a single unchecked stretch, and a lease sized for one job
/// would lapse partway through it while a sibling — correctly, by its own
/// reading of Redis — took over. Two coms consumers, which is the one thing
/// this whole mechanism exists to prevent. Bounding a serial drain to a single
/// job keeps the checked stretch equal to what the lease is actually sized
/// against; <see cref="StartAsync"/> enforces the other half of that
/// invariant.</para>
///
/// <para>A drain failure — a Redis blip, a broken envelope — is logged and the
/// loop continues. Background work must never take the host down.</para>
/// </summary>
public sealed partial class JobConsumerService(
    IJobConsumer consumer,
    IJobConnection connection,
    JobQueueTopology topology,
    ILogger<JobConsumerService> logger) : BackgroundService
{
    /// <summary>
    /// Jobs one drain may take. Bounded so a burst cannot starve the poll
    /// loop's cancellation — and bounded to ONE on a serial queue, so the work
    /// done on the strength of a single lease check is one job, which is the
    /// unit the lease TTL is sized against.
    /// </summary>
    private static int BatchSizeFor(JobQueueDefinition queue) => queue.IsSerial ? 1 : 10;

    /// <summary>
    /// Refuses to start on a configuration that cannot hold the guarantee it
    /// claims: a serial queue's lease has to outlive one of that queue's jobs,
    /// and <see cref="JobQueueDefinition.ClaimMinIdle"/> is already the
    /// deployment's stated upper bound on how long one job may take (it is what
    /// separates "still working" from "died"). If the lease is the shorter of
    /// the two, an ordinary slow job — not a pathological pause — loses it
    /// mid-flight and a sibling starts consuming alongside it. Better a host
    /// that will not start than a queue that is silently no longer serial.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var queue in topology.Queues.Where(q => q.IsSerial))
        {
            if (topology.LeaseTtl <= queue.ClaimMinIdle)
            {
                throw new InvalidOperationException(
                    $"Serial queue '{queue.Name}' has a consume lease of {topology.LeaseTtl} but allows a "
                    + $"job to run for up to {queue.ClaimMinIdle} (its ClaimMinIdle). The lease would lapse "
                    + "while the job is still running and a second consumer would start. Raise "
                    + "Queue:LeaseTtlMs above it, or lower the queue's ClaimMinIdle.");
            }
        }

        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(topology.Queues.Select(queue => RunQueueAsync(queue, stoppingToken)));

    private Task RunQueueAsync(JobQueueDefinition queue, CancellationToken ct) =>
        queue.IsSerial
            ? RunLoopAsync(
                queue,
                new QueueLease(
                    connection, topology.LeaseKey(queue.Name), topology.ConsumerName, topology.LeaseTtl),
                ct)
            : Task.WhenAll(Enumerable
                .Range(0, queue.Consumers)
                .Select(_ => RunLoopAsync(queue, lease: null, ct)));

    /// <summary>
    /// One consumer. With a <paramref name="lease"/> it drains only while it
    /// holds it; without one it always drains.
    /// </summary>
    private async Task RunLoopAsync(JobQueueDefinition queue, QueueLease? lease, CancellationToken ct)
    {
        var held = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var mayConsume = lease is null || await lease.TryHoldAsync().ConfigureAwait(false);
                    if (lease is not null && mayConsume != held)
                    {
                        held = mayConsume;
                        LogLease(logger, queue.Name, topology.ConsumerName, held ? "acquired" : "lost");
                    }

                    if (!mayConsume)
                    {
                        await Task.Delay(topology.PollInterval, ct).ConfigureAwait(false);
                        continue;
                    }

                    var processed = await consumer
                        .DrainOnceAsync(queue.Name, BatchSizeFor(queue), ct).ConfigureAwait(false);
                    if (processed == 0)
                    {
                        await Task.Delay(topology.PollInterval, ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    LogDrainFailed(logger, exception, queue.Name);
                    try
                    {
                        await Task.Delay(topology.PollInterval, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            // Shutting down: hand the lease back rather than making a sibling
            // wait out the TTL for a worker that is already gone.
            if (lease is not null)
            {
                await lease.ReleaseAsync().ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Consume lease for {Queue} {Transition} by {Consumer}")]
    private static partial void LogLease(
        ILogger logger, string queue, string consumer, string transition);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Draining the {Queue} job queue failed; the loop continues")]
    private static partial void LogDrainFailed(ILogger logger, Exception exception, string queue);
}
