using Hsm.Domain.Coms;

namespace Hsm.Application.Coms;

/// <summary>Persistence port for received webhook events.</summary>
public interface IEmailWebhookEventStore
{
    Task<EmailWebhookEvent?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The already-recorded (messageId, eventType) pairs for a provider among
    /// <paramref name="messageIds"/> — one query for the whole delivery's
    /// idempotency check.
    /// </summary>
    Task<IReadOnlyList<(string MessageId, string EventType)>> ExistingEventKeysAsync(
        string provider, IReadOnlyCollection<string> messageIds, CancellationToken ct = default);

    Task AddAsync(EmailWebhookEvent webhookEvent, CancellationToken ct = default);
}
