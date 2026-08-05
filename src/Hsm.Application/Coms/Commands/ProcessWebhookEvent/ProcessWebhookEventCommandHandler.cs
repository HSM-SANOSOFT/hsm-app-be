using Hsm.Application.Abstractions;
using Hsm.Application.Identity;
using Hsm.Application.Coms;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Commands.ProcessWebhookEvent;

/// <summary>
/// The 'process-webhook-event' processor: idempotent on processedAt, matches
/// the MOST RECENT recipient row by address, maps delivery-relevant event
/// types onto the recipient's status, writes the suppression row for hard
/// bounces and spam complaints (insert-or-ignore), and stamps processedAt —
/// all in one <c>SaveChangesAsync</c> call, which is atomic on its own (a
/// single call, relational provider), so no explicit transaction wrapper is
/// needed around it.
/// </summary>
public sealed class ProcessWebhookEventCommandHandler(
    IEmailWebhookEventStore events,
    IEmailBatchStore batches,
    IEmailSuppressionStore suppressions,
    IUnitOfWork unitOfWork) : IRequestHandler<ProcessWebhookEventCommand, Unit>
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

    /// <summary>Maps a webhook event type to a recipient status; null means no status change.</summary>
    private static string? MapEventTypeToRecipientStatus(string eventType) => eventType switch
    {
        EmailWebhookEventTypes.Delivered => EmailRecipientStatus.Delivered,
        EmailWebhookEventTypes.BouncedHard => EmailRecipientStatus.BouncedHard,
        EmailWebhookEventTypes.BouncedSoft => EmailRecipientStatus.BouncedSoft,
        EmailWebhookEventTypes.Spam => EmailRecipientStatus.Spam,
        _ => null,
    };
}
