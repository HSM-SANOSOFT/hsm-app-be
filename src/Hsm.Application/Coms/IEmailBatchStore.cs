using Hsm.Contracts;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms;

/// <summary>Filters for the frozen listBatches query.</summary>
public sealed record EmailListFilter(
    Guid? TemplateId,
    string? OverallStatus,
    Guid? CreatedBy,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate);

/// <summary>Persistence port for email batches and their recipients.</summary>
public interface IEmailBatchStore
{
    Task<EmailBatch?> FindAsync(Guid id, bool withRecipients = false, CancellationToken ct = default);

    /// <summary>createdAt DESC, offset pagination (frozen listBatches).</summary>
    Task<PagedResult<EmailBatch>> ListEmailsAsync(
        EmailListFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task AddAsync(EmailBatch batch, CancellationToken ct = default);

    Task<EmailRecipient?> FindRecipientAsync(Guid id, CancellationToken ct = default);

    /// <summary>The most recent recipient row (id DESC) for an address — the frozen webhook match rule.</summary>
    Task<EmailRecipient?> FindLatestRecipientByEmailAsync(string email, CancellationToken ct = default);
}
