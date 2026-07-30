using Hsm.Application.Abstractions;
using Hsm.Application.Docs;
using Hsm.Application.Docs.Commands.RenderDocument;
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
    public string ReserveGenerateDocumentJobId() => ReserveJobId();

    public Task EnqueueGenerateDocumentAsync(
        string jobId, GenerateDocumentJob job, CancellationToken ct = default) =>
        EnqueueReservedAsync(jobId, job, ct);
}

/// <summary>
/// The background generation processor (frozen worker DocsProcessorService):
/// consume loop, the frozen 1s enqueue delay, retries, and logging come from
/// the shared processor. Terminal failures are logged, never thrown into the
/// host (the document row already reads FAILED).
///
/// <b>Deliberately bypasses <see cref="IDispatcher"/></b> for
/// <see cref="RenderDocumentCommand"/>: it resolves the command's
/// <see cref="IRequestHandler{TRequest,TResult}"/> directly from the
/// per-attempt scope and calls <c>HandleAsync</c> on it, the same shape as
/// the pre-slicing code that called the old concrete
/// <c>GenerateDocumentJobHandler</c> class. This is the "keep the existing
/// mechanism" option from the Task 12 brief (Coms precedent), not an
/// oversight — see <see cref="RenderDocumentCommand"/>'s doc comment for the
/// concrete contract tests a naive <c>IDispatcher</c> route would break (a
/// pipeline-owned transaction rolling back the FAILED write a retry depends
/// on). Because of that, <see cref="RenderDocumentCommand"/>'s policy
/// attribute is honest, forward-looking metadata for Task 19 — not enforced
/// today.
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
        services.GetRequiredService<IRequestHandler<RenderDocumentCommand, Unit>>().HandleAsync(
            new RenderDocumentCommand(job.DocumentId, job.TemplateIdentifier, job.DataJson, job.EntityId, job.EntityType),
            ct);
}
