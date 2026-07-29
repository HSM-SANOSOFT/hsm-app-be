namespace Hsm.Application.Coms;

/// <summary>
/// The background job hand-off (frozen BullMQ 'coms' queue). Job ids are the
/// values the frozen API surfaced to clients; send-email ids can be reserved
/// ahead of enqueue so the id persists in the same transaction as its batch.
/// </summary>
public interface IComsJobDispatcher
{
    /// <summary>Mints the id the next send-email enqueue will carry, without enqueuing.</summary>
    string ReserveSendEmailJobId();

    Task EnqueueSendEmailAsync(
        string jobId, Guid batchId, Guid? recipientId = null, CancellationToken ct = default);

    Task<string> EnqueueProcessWebhookEventAsync(Guid webhookEventId, CancellationToken ct = default);
}
