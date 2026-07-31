using Hsm.Application.Ports;
using Hsm.Infrastructure.Jobs;

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
/// serialize the queue, while <c>coms</c> runs one, because the frozen resend
/// ordering depends on no newer job starting while an earlier one is still
/// working through its attempts.</para>
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
/// <para>A drain failure — a Redis blip, a broken envelope — is logged and the
/// loop continues. Background work must never take the host down.</para>
/// </summary>
public sealed partial class JobConsumerService(
    IJobConsumer consumer,
    IJobConnection connection,
    JobQueueTopology topology,
    ILogger<JobConsumerService> logger) : BackgroundService
{
    /// <summary>Jobs one drain may take. Bounded so a burst cannot starve the poll loop's cancellation.</summary>
    private const int BatchSize = 10;

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
                        .DrainOnceAsync(queue.Name, BatchSize, ct).ConfigureAwait(false);
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
