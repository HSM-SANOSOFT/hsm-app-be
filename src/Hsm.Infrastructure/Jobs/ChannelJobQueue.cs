using System.Globalization;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Jobs;

/// <summary>
/// Retry posture for an in-process channel queue (frozen BullMQ job options):
/// attempts, an optional delay before the first attempt, and the base delay
/// for exponential backoff between attempts.
/// </summary>
public class JobRetryOptions
{
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Delay before the FIRST attempt of each job (frozen BullMQ 'delay').</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.Zero;

    /// <summary>Base delay for exponential backoff between attempts.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Concurrent jobs the processor may run. 1 preserves the frozen strictly
    /// serial worker (required where jobs can target the same aggregate —
    /// coms send/resend on one batch); queues whose jobs are independent can
    /// raise it so one failing job's backoff does not serialize the queue.
    /// </summary>
    public int MaxParallelJobs { get; init; } = 1;
}

/// <summary>
/// WORKER-TOPOLOGY DECISION (plan U14, deferred to implementation): the
/// frozen system pushed background jobs through BullMQ/Redis to a separate
/// worker process — a queue hop whose main rationale (Node's single thread)
/// does not apply here. The work still needs genuinely asynchronous dispatch
/// (an HTTP request must return before SMTP/PDF round-trips), so processing
/// is COLLAPSED INTO THE HOST PROCESS: an in-process channel consumed by a
/// <see cref="ChannelJobProcessor{TJob}"/> on the thread pool, preserving the
/// frozen retry semantics. The hand-off stays behind each module's dispatcher
/// port, so a distributed queue adapter can replace this one — hosted by
/// Hsm.Worker, which registers the same processor — if cross-process dispatch
/// is ever needed again; nothing above the port would change.
/// Job ids are numeric strings, as BullMQ surfaced them to frozen clients.
/// </summary>
public abstract class ChannelJobDispatcher<TJob>
{
    private readonly Channel<(string JobId, TJob Job)> _channel =
        Channel.CreateUnbounded<(string, TJob)>();

    private long _nextJobId;

    internal ChannelReader<(string JobId, TJob Job)> Reader => _channel.Reader;

    /// <summary>
    /// Mints the next numeric job id WITHOUT enqueuing — lets a caller persist
    /// the id in the same transaction as the work it identifies, then enqueue
    /// after commit via <see cref="EnqueueReservedAsync"/>.
    /// </summary>
    protected string ReserveJobId() =>
        Interlocked.Increment(ref _nextJobId).ToString(CultureInfo.InvariantCulture);

    protected async Task EnqueueReservedAsync(string jobId, TJob job, CancellationToken ct = default) =>
        await _channel.Writer.WriteAsync((jobId, job), ct);

    protected async Task<string> EnqueueAsync(TJob job, CancellationToken ct = default)
    {
        var jobId = ReserveJobId();
        await EnqueueReservedAsync(jobId, job, ct);
        return jobId;
    }
}

/// <summary>
/// The background consumer (frozen worker QueueWorkerHost): one scope per
/// attempt, frozen retry posture (exponential backoff RetryBaseDelay *
/// 2^(attempt-1)), terminal failures logged — never thrown into the host.
/// Parallelism is per-queue configuration (<see cref="JobRetryOptions.MaxParallelJobs"/>).
/// </summary>
public abstract class ChannelJobProcessor<TJob>(
    ChannelJobDispatcher<TJob> queue,
    IServiceScopeFactory scopeFactory,
    JobRetryOptions options,
    ILogger logger) : BackgroundService
{
    /// <summary>The queue name carried in retry/failure log lines.</summary>
    protected abstract string QueueName { get; }

    /// <summary>One attempt of one job, inside its own service scope.</summary>
    protected abstract Task RunAsync(TJob job, IServiceProvider services, CancellationToken ct);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Parallel.ForEachAsync(
            queue.Reader.ReadAllAsync(stoppingToken),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxParallelJobs,
                CancellationToken = stoppingToken,
            },
            async (item, ct) => await RunWithRetriesAsync(item.JobId, item.Job, ct));

    private async Task RunWithRetriesAsync(string jobId, TJob job, CancellationToken ct)
    {
        if (options.InitialDelay > TimeSpan.Zero)
        {
            try
            {
                // Frozen BullMQ delay: the first attempt starts after the
                // configured delay — applied per job, not on the consume loop.
                await Task.Delay(options.InitialDelay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await RunAsync(job, scope.ServiceProvider, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (attempt == options.MaxAttempts)
                {
                    ChannelJobLog.JobFailed(logger, exception, QueueName, jobId, attempt);
                    return;
                }

                ChannelJobLog.JobRetry(logger, QueueName, jobId, attempt, exception.Message);
                try
                {
                    // Frozen: exponential backoff (delay * 2^(attempt-1)).
                    await Task.Delay(options.RetryBaseDelay * Math.Pow(2, attempt - 1), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}

/// <summary>LoggerMessage pair for <see cref="ChannelJobProcessor{TJob}"/> (the
/// source generator does not support generic containing types).</summary>
internal static partial class ChannelJobLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "{QueueName} job {JobId} failed after {Attempts} attempts")]
    internal static partial void JobFailed(ILogger logger, Exception exception, string queueName, string jobId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{QueueName} job {JobId} attempt {Attempt} failed: {Reason}; retrying")]
    internal static partial void JobRetry(ILogger logger, string queueName, string jobId, int attempt, string reason);
}

/// <summary>DI shape shared by the channel queues: the concrete dispatcher as
/// a singleton, the module port aliased onto it, and the processor hosted.</summary>
public static class ChannelJobQueueRegistration
{
    public static IServiceCollection AddChannelJobQueue<TPort, TDispatcher, TProcessor>(
        this IServiceCollection services)
        where TPort : class
        where TDispatcher : class, TPort
        where TProcessor : BackgroundService
    {
        services.AddSingleton<TDispatcher>();
        services.AddSingleton<TPort>(sp => sp.GetRequiredService<TDispatcher>());
        services.AddHostedService<TProcessor>();
        return services;
    }
}
