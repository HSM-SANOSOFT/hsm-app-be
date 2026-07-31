using Hsm.Application.Ports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Jobs;

/// <summary>
/// The consume loop: one <c>while</c> per configured consumer around
/// <see cref="IJobConsumer.DrainOnceAsync"/>.
///
/// <para>Parallelism is per-queue configuration
/// (<see cref="JobQueueDefinition.Consumers"/>): <c>coms</c> runs ONE loop
/// because the frozen resend ordering depends on serial processing, while
/// <c>docs</c> runs four because generation jobs are independent and one job's
/// backoff must not serialize the queue.</para>
///
/// <para>A drain failure — a Redis blip, a broken envelope — is logged and the
/// loop continues. Background work must never take the host down.</para>
/// </summary>
public sealed partial class JobConsumerHostedService(
    IJobConsumer consumer,
    JobQueueTopology topology,
    ILogger<JobConsumerHostedService> logger) : BackgroundService
{
    /// <summary>Jobs one drain may take. Bounded so a burst cannot starve the poll loop's cancellation.</summary>
    private const int BatchSize = 10;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(topology.Queues
            .SelectMany(queue => Enumerable
                .Range(0, Math.Max(1, queue.Consumers))
                .Select(_ => RunAsync(queue, stoppingToken))));

    private async Task RunAsync(JobQueueDefinition queue, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var processed = await consumer.DrainOnceAsync(queue.Name, BatchSize, ct).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Draining the {Queue} job queue failed; the loop continues")]
    private static partial void LogDrainFailed(ILogger logger, Exception exception, string queue);
}
