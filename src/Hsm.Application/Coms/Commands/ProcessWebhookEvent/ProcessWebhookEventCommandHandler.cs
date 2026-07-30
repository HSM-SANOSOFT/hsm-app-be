using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Coms;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Commands.ProcessWebhookEvent;

/// <summary>
/// The frozen worker 'process-webhook-event' processor: idempotent on
/// processedAt, matches the MOST RECENT recipient row by address, maps
/// delivery-relevant event types onto the recipient's status, writes the
/// suppression row for hard bounces and spam complaints (insert-or-ignore),
/// and stamps processedAt — all in one <c>SaveChangesAsync</c> call, which is
/// atomic on its own (a single call, relational provider) — the frozen
/// <c>ExecuteInTransactionAsync</c> wrapper around it added no additional
/// atomicity and is not carried forward.
/// </summary>
/// <remarks>
/// The frozen service also enqueued a 'send-alert' job afterwards, which the
/// frozen worker never handled (every alert job failed as unknown). That dead
/// enqueue is deliberately not ported.
/// </remarks>
public sealed class ProcessWebhookEventCommandHandler(
    IEmailWebhookEventStore events,
    IEmailBatchStore batches,
    IEmailSuppressionStore suppressions,
    IAuthUnitOfWork unitOfWork) : IRequestHandler<ProcessWebhookEventCommand, Unit>
{
    public async Task<Unit> HandleAsync(ProcessWebhookEventCommand request, CancellationToken ct)
    {
        var webhookEvent = await events.FindAsync(request.WebhookEventId, ct);
        if (webhookEvent is null || webhookEvent.ProcessedAt is not null)
        {
            return Unit.Value;
        }

        var recipient = await batches.FindLatestRecipientByEmailAsync(webhookEvent.RecipientEmail, ct);

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
                ct);
        }

        webhookEvent.ProcessedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);

        return Unit.Value;
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
