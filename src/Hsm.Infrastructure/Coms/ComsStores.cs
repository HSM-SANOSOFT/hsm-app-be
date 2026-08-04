using Hsm.Application.Coms;
using Hsm.Contracts;
using Hsm.Domain.Coms;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Coms;

/// <summary>EF Core adapter for <see cref="IEmailBatchStore"/>.</summary>
public sealed class EmailBatchStore(HsmDbContext db) : IEmailBatchStore
{
    public async Task<EmailBatch?> FindAsync(
        Guid id, bool withRecipients = false, CancellationToken ct = default)
    {
        IQueryable<EmailBatch> query = db.EmailBatches;
        if (withRecipients)
        {
            query = query.Include(b => b.Recipients);
        }

        return await query.FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<PagedResult<EmailBatch>> ListEmailsAsync(
        EmailListFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        // Included even for the list view: Hsm.Api.Emails.EmailResource
        // reports TotalRecipients/SentCount/FailedCount per row, and those
        // are computed off the loaded Recipients collection — without this,
        // every row in the list would silently report zero for all three.
        IQueryable<EmailBatch> query = db.EmailBatches.AsNoTracking().Include(b => b.Recipients);
        if (filter.TemplateId is not null)
        {
            query = query.Where(b => b.TemplateId == filter.TemplateId);
        }

        if (filter.OverallStatus is not null)
        {
            query = query.Where(b => b.OverallStatus == filter.OverallStatus);
        }

        if (filter.CreatedBy is not null)
        {
            query = query.Where(b => b.CreatedBy == filter.CreatedBy);
        }

        if (filter.FromDate is not null && filter.ToDate is not null)
        {
            // Frozen: the date range filters only when BOTH bounds are given.
            query = query.Where(b => b.CreatedAt >= filter.FromDate && b.CreatedAt <= filter.ToDate);
        }

        var totalItems = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new PagedResult<EmailBatch>(items, page, pageSize, totalItems);
    }

    public async Task AddAsync(EmailBatch batch, CancellationToken ct = default) =>
        await db.EmailBatches.AddAsync(batch, ct);

    public async Task<EmailRecipient?> FindRecipientAsync(Guid id, CancellationToken ct = default) =>
        await db.EmailRecipients.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<EmailRecipient?> FindLatestRecipientByEmailAsync(
        string email, CancellationToken ct = default) =>
        // The frozen match rule: the most recent recipient row for the
        // address (its "id DESC" over uuid keys was an accidental proxy for
        // recency; the batch's creation time is the real signal).
        await db.EmailRecipients
            .Where(r => r.ToEmail == email)
            .Join(db.EmailBatches, r => r.BatchId, b => b.Id, (r, b) => new { Recipient = r, b.CreatedAt })
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Recipient.Id)
            .Select(x => x.Recipient)
            .FirstOrDefaultAsync(ct);
}

/// <summary>EF Core adapter for <see cref="IEmailSuppressionStore"/>.</summary>
public sealed class EmailSuppressionStore(HsmDbContext db) : IEmailSuppressionStore
{
    public async Task<IReadOnlyList<string>> SuppressedAmongAsync(
        IReadOnlyCollection<string> emails, CancellationToken ct = default) =>
        emails.Count == 0
            ? []
            : await db.EmailSuppressions
                .Where(s => emails.Contains(s.Email))
                .Select(s => s.Email)
                .ToListAsync(ct);

    public async Task AddIfMissingAsync(EmailSuppression suppression, CancellationToken ct = default)
    {
        // The frozen orIgnore upsert: first suppression wins.
        if (await db.EmailSuppressions.AnyAsync(s => s.Email == suppression.Email, ct))
        {
            return;
        }

        await db.EmailSuppressions.AddAsync(suppression, ct);
    }
}

/// <summary>EF Core adapter for <see cref="IEmailWebhookEventStore"/>.</summary>
public sealed class EmailWebhookEventStore(HsmDbContext db) : IEmailWebhookEventStore
{
    public async Task<EmailWebhookEvent?> FindAsync(Guid id, CancellationToken ct = default) =>
        await db.EmailWebhookEvents.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<(string MessageId, string EventType)>> ExistingEventKeysAsync(
        string provider, IReadOnlyCollection<string> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        var rows = await db.EmailWebhookEvents
            .Where(e => e.Provider == provider && e.MessageId != null && messageIds.Contains(e.MessageId))
            .Select(e => new { e.MessageId, e.EventType })
            .ToListAsync(ct);
        return [.. rows.Select(r => (r.MessageId!, r.EventType))];
    }

    public async Task AddAsync(EmailWebhookEvent webhookEvent, CancellationToken ct = default) =>
        await db.EmailWebhookEvents.AddAsync(webhookEvent, ct);
}
