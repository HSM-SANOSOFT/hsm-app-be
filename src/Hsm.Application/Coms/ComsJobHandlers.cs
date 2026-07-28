using System.Text.Json.Nodes;
using Hsm.Application.Auth;
using Hsm.Application.Templates;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms;

/// <summary>
/// The frozen worker 'send-email' processor (worker EmailService.sendEmail):
/// batch → PROCESSING, target selection (one recipient on a recipient resend,
/// otherwise every PENDING/FAILED row — SUPPRESSED rows are never targeted),
/// template render (failures leave recipient rows untouched and fail the job
/// for retry), one transport send, then SENT/FAILED bulk updates and the
/// aggregate overall-status recomputation.
/// </summary>
public sealed class SendEmailJobHandler(
    IEmailBatchStore batches,
    TemplateParser parser,
    IEmailTransport transport,
    IAuthUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid batchId, Guid? recipientId, CancellationToken ct = default)
    {
        var batch = await batches.FindAsync(batchId, withRecipients: true, ct)
            ?? throw new InvalidOperationException($"EmailBatch {batchId} not found");

        batch.OverallStatus = EmailBatchStatus.Processing;
        await unitOfWork.SaveChangesAsync(ct);

        var targets = recipientId is not null
            ? batch.Recipients.Where(r => r.Id == recipientId).ToList()
            : batch.Recipients
                .Where(r => r.Status is EmailRecipientStatus.Pending or EmailRecipientStatus.Failed)
                .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        // Frozen: attachment resolution consulted the documents module, which
        // arrives with U15 — documentIds round-trip untouched until then.

        // Render BEFORE the failure-marking scope: a template problem retries
        // the job without touching recipient rows (frozen behavior).
        var data = JsonNode.Parse(batch.DataJson) as JsonObject ?? [];
        var render = await parser.ParseEmailAsync(
            batch.TemplateId?.ToString() ?? string.Empty, data, userId: null, ct);

        try
        {
            var messageId = await transport.SendAsync(
                new OutboundEmail(
                    batch.FromEmail,
                    [.. targets.Select(t => t.ToEmail)],
                    render.Subject,
                    render.Html),
                ct);

            var now = DateTimeOffset.UtcNow;
            foreach (var target in targets)
            {
                target.Status = EmailRecipientStatus.Sent;
                target.SentAt = now;
            }

            batch.ProviderMessageId = messageId;
            batch.OverallStatus = ComputeOverallStatus(batch.Recipients);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            foreach (var target in targets)
            {
                target.Status = EmailRecipientStatus.Failed;
                target.ErrorMessage = exception.Message;
            }

            batch.OverallStatus = ComputeOverallStatus(batch.Recipients);
            await unitOfWork.SaveChangesAsync(ct);
            throw;
        }
    }

    /// <summary>The frozen computeOverallStatus (SENT and DELIVERED both count as sent).</summary>
    internal static string ComputeOverallStatus(IReadOnlyCollection<EmailRecipient> recipients)
    {
        var sent = recipients.Count(r =>
            r.Status is EmailRecipientStatus.Sent or EmailRecipientStatus.Delivered);
        var failed = recipients.Count(r => r.Status == EmailRecipientStatus.Failed);
        var total = recipients.Count;

        if (sent == total)
        {
            return EmailBatchStatus.Sent;
        }

        if (failed == total)
        {
            return EmailBatchStatus.Failed;
        }

        return sent > 0 ? EmailBatchStatus.Partial : EmailBatchStatus.Pending;
    }
}

/// <summary>
/// The frozen worker 'process-webhook-event' processor: idempotent on
/// processedAt, matches the MOST RECENT recipient row by address, maps
/// delivery-relevant event types onto the recipient's status, writes the
/// suppression row for hard bounces and spam complaints (insert-or-ignore),
/// and stamps processedAt — all in one transaction.
/// </summary>
/// <remarks>
/// The frozen service also enqueued a 'send-alert' job afterwards, which the
/// frozen worker never handled (every alert job failed as unknown). That dead
/// enqueue is deliberately not ported.
/// </remarks>
public sealed class ProcessWebhookJobHandler(
    IEmailWebhookEventStore events,
    IEmailBatchStore batches,
    IEmailSuppressionStore suppressions,
    IAuthUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid webhookEventId, CancellationToken ct = default)
    {
        var webhookEvent = await events.FindAsync(webhookEventId, ct);
        if (webhookEvent is null || webhookEvent.ProcessedAt is not null)
        {
            return;
        }

        var recipient = await batches.FindLatestRecipientByEmailAsync(webhookEvent.RecipientEmail, ct);

        await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                if (recipient is not null)
                {
                    var mapped = MapEventTypeToRecipientStatus(webhookEvent.EventType);
                    if (mapped is not null)
                    {
                        recipient.Status = mapped;
                    }

                    webhookEvent.RecipientId = recipient.Id;
                }

                if (webhookEvent.EventType is EmailWebhookEventTypes.BouncedHard or EmailWebhookEventTypes.Spam)
                {
                    await suppressions.AddIfMissingAsync(
                        new EmailSuppression
                        {
                            Id = Guid.NewGuid(),
                            Email = webhookEvent.RecipientEmail,
                            Reason = webhookEvent.EventType == EmailWebhookEventTypes.Spam
                                ? EmailSuppressionReasons.SpamComplaint
                                : EmailSuppressionReasons.HardBounce,
                            SourceWebhookEventId = webhookEvent.Id,
                            CreatedAt = DateTimeOffset.UtcNow,
                        },
                        innerCt);
                }

                webhookEvent.ProcessedAt = DateTimeOffset.UtcNow;
                await unitOfWork.SaveChangesAsync(innerCt);
                return true;
            },
            ct);
    }

    /// <summary>The frozen event→recipient status map; null means no status change.</summary>
    private static string? MapEventTypeToRecipientStatus(string eventType) => eventType switch
    {
        EmailWebhookEventTypes.Delivered => EmailRecipientStatus.Delivered,
        EmailWebhookEventTypes.BouncedHard => EmailRecipientStatus.BouncedHard,
        EmailWebhookEventTypes.BouncedSoft => EmailRecipientStatus.BouncedSoft,
        EmailWebhookEventTypes.Spam => EmailRecipientStatus.Spam,
        _ => null,
    };
}
