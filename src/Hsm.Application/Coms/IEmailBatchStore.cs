using Hsm.Contracts;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms;

/// <summary>Filters for the list-batches query.</summary>
public sealed record EmailListFilter(
    Guid? TemplateId,
    string? OverallStatus,
    Guid? CreatedBy,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate);

/// <summary>
/// A batch as the LIST view needs it: the batch row, plus the three recipient
/// counts the wire shape reports — never the recipient rows themselves.
///
/// <para>This type exists so the list query can compute those counts with SQL
/// aggregation. Loading <c>EmailBatch.Recipients</c> to count them in memory
/// makes a page of batches materialize every recipient row on the page: a
/// hundred batches of a thousand recipients each is a hundred thousand rows
/// read to produce three hundred integers.</para>
/// </summary>
public sealed record EmailBatchSummary(
    EmailBatch Batch, int TotalRecipients, int SentCount, int FailedCount)
{
    /// <summary>
    /// The same three counts taken from an ALREADY-loaded recipient collection —
    /// for the detail view, which has to load them anyway to render them, so
    /// counting in memory there costs nothing extra.
    /// </summary>
    public static EmailBatchSummary From(EmailBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return new EmailBatchSummary(
            batch,
            batch.Recipients.Count,
            // SENT and DELIVERED both count as sent (DispatchEmailBatchHandler's
            // own rule — a recipient moves SENT -> DELIVERED when the provider's
            // webhook lands, and that is not a regression back to "not sent").
            // EmailBatchStore.ListEmailsAsync states the same rule as SQL;
            // EmailsEndpointTests.The_lists_recipient_counts_are_the_details_own_counts
            // pins the two forms against each other.
            batch.Recipients.Count(r =>
                r.Status is EmailRecipientStatus.Sent or EmailRecipientStatus.Delivered),
            batch.Recipients.Count(r => r.Status == EmailRecipientStatus.Failed));
    }
}

/// <summary>Persistence port for email batches and their recipients.</summary>
public interface IEmailBatchStore
{
    Task<EmailBatch?> FindAsync(Guid id, bool withRecipients = false, CancellationToken ct = default);

    /// <summary>
    /// createdAt DESC, offset pagination. Returns summaries, not aggregates —
    /// see <see cref="EmailBatchSummary"/> for why the recipient rows stay in
    /// the database.
    /// </summary>
    Task<PagedResult<EmailBatchSummary>> ListEmailsAsync(
        EmailListFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task AddAsync(EmailBatch batch, CancellationToken ct = default);

    Task<EmailRecipient?> FindRecipientAsync(Guid id, CancellationToken ct = default);

    /// <summary>The most recent recipient row (id DESC) for an address — the webhook match rule.</summary>
    Task<EmailRecipient?> FindLatestRecipientByEmailAsync(string email, CancellationToken ct = default);
}
