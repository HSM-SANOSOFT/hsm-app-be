using Hsm.Application.Docs;
using Hsm.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Docs;

/// <summary>
/// Retry posture for document generation (frozen BullMQ 'document' queue
/// options: attempts 3, initial delay 1000ms, backoff 2000ms exponential).
/// </summary>
public sealed class DocsQueueOptions : JobRetryOptions
{
    public DocsQueueOptions()
    {
        MaxAttempts = 3;
        InitialDelay = TimeSpan.FromSeconds(1);
        RetryBaseDelay = TimeSpan.FromSeconds(2);
        // Generation jobs are independent (one document each): bounded
        // parallelism keeps a failing job's backoff — and the frozen 1s
        // initial delay — from serializing the queue.
        MaxParallelJobs = 4;
    }
}

/// <summary>
/// The in-process docs queue (see <see cref="ChannelJobDispatcher{TJob}"/>
/// for the worker-topology decision) behind the
/// <see cref="IDocsJobDispatcher"/> port.
/// </summary>
public sealed class ChannelDocsDispatcher : ChannelJobDispatcher<GenerateDocumentJob>, IDocsJobDispatcher
{
    public Task<string> EnqueueGenerateDocumentAsync(
        GenerateDocumentJob job, CancellationToken ct = default) => EnqueueAsync(job, ct);
}

/// <summary>
/// The background generation processor (frozen worker DocsProcessorService):
/// consume loop, the frozen 1s enqueue delay, retries, and logging come from
/// the shared processor. Terminal failures are logged, never thrown into the
/// host (the document row already reads FAILED).
/// </summary>
public sealed class DocsJobProcessor(
    ChannelDocsDispatcher queue,
    IServiceScopeFactory scopeFactory,
    DocsQueueOptions options,
    ILogger<DocsJobProcessor> logger)
    : ChannelJobProcessor<GenerateDocumentJob>(queue, scopeFactory, options, logger)
{
    protected override string QueueName => "Docs";

    protected override Task RunAsync(GenerateDocumentJob job, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<GenerateDocumentJobHandler>().HandleAsync(job, ct);
}
