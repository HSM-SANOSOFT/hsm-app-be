using Hsm.Domain.Coms;

namespace Hsm.Application.Coms;

/// <summary>Filters for the frozen listBatches query.</summary>
public sealed record BatchListFilter(
    Guid? TemplateId,
    string? OverallStatus,
    Guid? CreatedBy,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    int Page,
    int Limit);

/// <summary>Filters for the frozen listRecipients query.</summary>
public sealed record RecipientListFilter(
    Guid? BatchId,
    string? ToEmail,
    string? Status,
    int Page,
    int Limit);

/// <summary>Persistence port for email batches and their recipients.</summary>
public interface IEmailBatchStore
{
    Task<EmailBatch?> FindAsync(Guid id, bool withRecipients = false, CancellationToken ct = default);

    /// <summary>createdAt DESC, offset pagination (frozen listBatches).</summary>
    Task<IReadOnlyList<EmailBatch>> ListAsync(BatchListFilter filter, CancellationToken ct = default);

    Task AddAsync(EmailBatch batch, CancellationToken ct = default);

    Task<EmailRecipient?> FindRecipientAsync(Guid id, CancellationToken ct = default);

    /// <summary>id ASC, offset pagination (frozen listRecipients).</summary>
    Task<IReadOnlyList<EmailRecipient>> ListRecipientsAsync(
        RecipientListFilter filter, CancellationToken ct = default);

    /// <summary>The most recent recipient row (id DESC) for an address — the frozen webhook match rule.</summary>
    Task<EmailRecipient?> FindLatestRecipientByEmailAsync(string email, CancellationToken ct = default);
}

/// <summary>Persistence port for the suppression list.</summary>
public interface IEmailSuppressionStore
{
    /// <summary>The subset of <paramref name="emails"/> present on the suppression list.</summary>
    Task<IReadOnlyList<string>> SuppressedAmongAsync(
        IReadOnlyCollection<string> emails, CancellationToken ct = default);

    /// <summary>Insert-or-ignore against the unique email constraint (first suppression wins).</summary>
    Task AddIfMissingAsync(EmailSuppression suppression, CancellationToken ct = default);
}

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

/// <summary>An outbound email as handed to the provider transport.</summary>
public sealed record OutboundEmail(
    string? From, IReadOnlyList<string> To, string Subject, string Html);

/// <summary>
/// Actual SMTP delivery is deployment configuration: this port is implemented
/// by a logging adapter in this repo (a real relay adapter is wired per
/// deployment), returning the provider message id.
/// </summary>
public interface IEmailTransport
{
    Task<string> SendAsync(OutboundEmail email, CancellationToken ct = default);
}
