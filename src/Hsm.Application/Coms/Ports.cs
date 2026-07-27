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

    /// <summary>All recipient rows of a batch (for overall-status recomputation).</summary>
    Task<IReadOnlyList<EmailRecipient>> RecipientsOfBatchAsync(Guid batchId, CancellationToken ct = default);
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

    /// <summary>True when an event with the same provider/messageId/eventType is already recorded.</summary>
    Task<bool> DuplicateExistsAsync(
        string provider, string messageId, string eventType, CancellationToken ct = default);

    Task AddAsync(EmailWebhookEvent webhookEvent, CancellationToken ct = default);
}

/// <summary>
/// The background job hand-off (frozen BullMQ 'coms' queue). Enqueue returns
/// the job id the frozen API surfaced to clients.
/// </summary>
public interface IComsJobDispatcher
{
    Task<string> EnqueueSendEmailAsync(Guid batchId, Guid? recipientId = null, CancellationToken ct = default);

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
