using Hsm.Application.Coms;
using Hsm.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Coms;

/// <summary>One queued communications job (frozen BullMQ 'coms' queue job).</summary>
public abstract record ComsJob
{
    public sealed record SendEmail(Guid BatchId, Guid? RecipientId) : ComsJob;

    public sealed record ProcessWebhookEvent(Guid WebhookEventId) : ComsJob;
}

/// <summary>
/// Retry posture for the coms dispatcher (frozen BullMQ job options: attempts
/// 5 for both send-email and process-webhook-event, backoff 5000ms/3000ms
/// exponential).
/// </summary>
public sealed class ComsQueueOptions : JobRetryOptions
{
    public ComsQueueOptions()
    {
        MaxAttempts = 5;
        RetryBaseDelay = TimeSpan.FromSeconds(5);
        // Strictly serial (frozen worker): send and resend jobs can target
        // the SAME batch — concurrent processing lets a retrying job's stale
        // FAILED write land after a resend's SENT write.
        MaxParallelJobs = 1;
    }
}

/// <summary>
/// The in-process coms queue (see <see cref="ChannelJobDispatcher{TJob}"/>
/// for the worker-topology decision) behind the
/// <see cref="IComsJobDispatcher"/> port.
/// </summary>
public sealed class ChannelComsDispatcher : ChannelJobDispatcher<ComsJob>, IComsJobDispatcher
{
    public string ReserveSendEmailJobId() => ReserveJobId();

    public Task EnqueueSendEmailAsync(
        string jobId, Guid batchId, Guid? recipientId = null, CancellationToken ct = default) =>
        EnqueueReservedAsync(jobId, new ComsJob.SendEmail(batchId, recipientId), ct);

    public Task<string> EnqueueProcessWebhookEventAsync(
        Guid webhookEventId, CancellationToken ct = default) =>
        EnqueueAsync(new ComsJob.ProcessWebhookEvent(webhookEventId), ct);
}

/// <summary>
/// The background dispatcher (frozen worker ComsService/QueueWorkerHost):
/// consume loop, retries, and logging come from the shared processor.
/// </summary>
public sealed class ComsJobProcessor(
    ChannelComsDispatcher queue,
    IServiceScopeFactory scopeFactory,
    ComsQueueOptions options,
    ILogger<ComsJobProcessor> logger)
    : ChannelJobProcessor<ComsJob>(queue, scopeFactory, options, logger)
{
    protected override string QueueName => "Coms";

    protected override async Task RunAsync(ComsJob job, IServiceProvider services, CancellationToken ct)
    {
        switch (job)
        {
            case ComsJob.SendEmail send:
                await services.GetRequiredService<SendEmailJobHandler>()
                    .HandleAsync(send.BatchId, send.RecipientId, ct);
                break;
            case ComsJob.ProcessWebhookEvent webhook:
                await services.GetRequiredService<ProcessWebhookJobHandler>()
                    .HandleAsync(webhook.WebhookEventId, ct);
                break;
            default:
                throw new InvalidOperationException($"Unknown job type: {job.GetType().Name}");
        }
    }
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
