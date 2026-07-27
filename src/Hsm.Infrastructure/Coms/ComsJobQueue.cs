using System.Threading.Channels;
using Hsm.Application.Coms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Coms;

/// <summary>One queued communications job (frozen BullMQ 'coms' queue job).</summary>
public abstract record ComsJob(string JobId)
{
    public sealed record SendEmail(string JobId, Guid BatchId, Guid? RecipientId) : ComsJob(JobId);

    public sealed record ProcessWebhookEvent(string JobId, Guid WebhookEventId) : ComsJob(JobId);
}

/// <summary>Retry posture for the dispatcher (frozen BullMQ job options).</summary>
public sealed class ComsQueueOptions
{
    /// <summary>Frozen attempts: 5 for both send-email and process-webhook-event.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Base delay for exponential backoff (frozen: 5000ms send / 3000ms webhook).</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// WORKER-TOPOLOGY DECISION (plan U14, deferred to implementation): the
/// frozen system pushed sends through BullMQ/Redis to a separate worker
/// process — a queue hop whose main rationale (Node's single thread) does not
/// apply here. Communications still need genuinely asynchronous dispatch
/// (an HTTP send must return before SMTP round-trips), so the send processing
/// is COLLAPSED INTO THE HOST PROCESS: an in-process channel consumed by
/// <see cref="ComsJobProcessor"/> on the thread pool, preserving the frozen
/// retry semantics (5 attempts, exponential backoff). The hand-off stays
/// behind the <see cref="IComsJobDispatcher"/> port, so a distributed queue
/// adapter can replace this one — hosted by Hsm.Worker, which registers the
/// same processor — if cross-process dispatch is ever needed again; nothing
/// above the port would change.
/// </summary>
public sealed class ChannelComsDispatcher : IComsJobDispatcher
{
    private readonly Channel<ComsJob> _channel = Channel.CreateUnbounded<ComsJob>();
    private long _nextJobId;

    public ChannelReader<ComsJob> Reader => _channel.Reader;

    public async Task<string> EnqueueSendEmailAsync(
        Guid batchId, Guid? recipientId = null, CancellationToken ct = default)
    {
        // Numeric-string ids, as BullMQ surfaced them to frozen clients.
        var jobId = Interlocked.Increment(ref _nextJobId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _channel.Writer.WriteAsync(new ComsJob.SendEmail(jobId, batchId, recipientId), ct);
        return jobId;
    }

    public async Task<string> EnqueueProcessWebhookEventAsync(
        Guid webhookEventId, CancellationToken ct = default)
    {
        var jobId = Interlocked.Increment(ref _nextJobId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _channel.Writer.WriteAsync(new ComsJob.ProcessWebhookEvent(jobId, webhookEventId), ct);
        return jobId;
    }
}

/// <summary>
/// The background dispatcher (frozen worker ComsService/QueueWorkerHost): one
/// scope per attempt, frozen retry posture, terminal failures logged — never
/// thrown into the host.
/// </summary>
public sealed partial class ComsJobProcessor(
    ChannelComsDispatcher queue,
    IServiceScopeFactory scopeFactory,
    ComsQueueOptions options,
    ILogger<ComsJobProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await RunWithRetriesAsync(job, stoppingToken);
        }
    }

    private async Task RunWithRetriesAsync(ComsJob job, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            try
            {
                await RunAsync(job, ct);
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
                    LogJobFailed(logger, exception, job.JobId, attempt);
                    return;
                }

                LogJobRetry(logger, job.JobId, attempt, exception.Message);
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

    private async Task RunAsync(ComsJob job, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        switch (job)
        {
            case ComsJob.SendEmail send:
                await scope.ServiceProvider.GetRequiredService<SendEmailJobHandler>()
                    .HandleAsync(send.BatchId, send.RecipientId, ct);
                break;
            case ComsJob.ProcessWebhookEvent webhook:
                await scope.ServiceProvider.GetRequiredService<ProcessWebhookJobHandler>()
                    .HandleAsync(webhook.WebhookEventId, ct);
                break;
            default:
                throw new InvalidOperationException($"Unknown job type: {job.GetType().Name}");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Coms job {JobId} failed after {Attempts} attempts")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, string jobId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Coms job {JobId} attempt {Attempt} failed: {Reason}; retrying")]
    private static partial void LogJobRetry(ILogger logger, string jobId, int attempt, string reason);
}

/// <summary>
/// The default <see cref="IEmailTransport"/>: SMTP delivery is deployment
/// configuration (frozen SMTP_* settings); in this repo the adapter logs the
/// send and fabricates a provider message id. Real relays are wired per
/// deployment behind the same port.
/// </summary>
public sealed partial class LoggingEmailTransport(ILogger<LoggingEmailTransport> logger) : IEmailTransport
{
    public Task<string> SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        var messageId = $"<{Guid.NewGuid():N}@hsm.local>";
        var recipients = string.Join(", ", email.To);
        LogSend(logger, recipients, email.Subject, messageId);
        return Task.FromResult(messageId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email dispatched to {To} (subject: {Subject}, messageId: {MessageId})")]
    private static partial void LogSend(ILogger logger, string to, string subject, string messageId);
}
