using System.Threading.Channels;
using Hsm.Application.Docs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Docs;

/// <summary>
/// Retry posture for document generation (frozen BullMQ 'document' queue
/// options: attempts 3, initial delay 1000ms, backoff 2000ms exponential).
/// </summary>
public sealed class DocsQueueOptions
{
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Frozen delay: 1000ms before the first attempt.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Base delay for exponential backoff between attempts (frozen: 2000ms).</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Same worker-topology decision as <see cref="Coms.ChannelComsDispatcher"/>
/// (see its remarks): the frozen BullMQ hop to a separate worker process is
/// collapsed into an in-process channel behind the
/// <see cref="IDocsJobDispatcher"/> port — a distributed adapter hosted by
/// Hsm.Worker could replace it without touching anything above the port.
/// Job ids are numeric strings, as BullMQ surfaced them to frozen clients.
/// </summary>
public sealed class ChannelDocsDispatcher : IDocsJobDispatcher
{
    private readonly Channel<(string JobId, GenerateDocumentJob Job)> _channel =
        Channel.CreateUnbounded<(string, GenerateDocumentJob)>();

    private long _nextJobId;

    public ChannelReader<(string JobId, GenerateDocumentJob Job)> Reader => _channel.Reader;

    public async Task<string> EnqueueGenerateDocumentAsync(
        GenerateDocumentJob job, CancellationToken ct = default)
    {
        var jobId = Interlocked.Increment(ref _nextJobId)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _channel.Writer.WriteAsync((jobId, job), ct);
        return jobId;
    }
}

/// <summary>
/// The background generation processor (frozen worker DocsProcessorService):
/// one scope per attempt, frozen retry posture, terminal failures logged —
/// never thrown into the host (the document row already reads FAILED).
/// </summary>
public sealed partial class DocsJobProcessor(
    ChannelDocsDispatcher queue,
    IServiceScopeFactory scopeFactory,
    DocsQueueOptions options,
    ILogger<DocsJobProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (jobId, job) in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await RunWithRetriesAsync(jobId, job, stoppingToken);
        }
    }

    private async Task RunWithRetriesAsync(string jobId, GenerateDocumentJob job, CancellationToken ct)
    {
        try
        {
            // Frozen BullMQ delay: the first attempt starts ~1s after enqueue.
            await Task.Delay(options.InitialDelay, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<GenerateDocumentJobHandler>()
                    .HandleAsync(job, ct);
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
                    LogJobFailed(logger, exception, jobId, attempt);
                    return;
                }

                LogJobRetry(logger, jobId, attempt, exception.Message);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Docs job {JobId} failed after {Attempts} attempts")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, string jobId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Docs job {JobId} attempt {Attempt} failed: {Reason}; retrying")]
    private static partial void LogJobRetry(ILogger logger, string jobId, int attempt, string reason);
}
