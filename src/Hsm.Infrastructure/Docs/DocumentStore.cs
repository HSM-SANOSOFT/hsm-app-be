using Hsm.Application.Docs;
using Hsm.Contracts;
using Hsm.Domain.Docs;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Docs;

/// <summary>
/// EF Core adapter for <see cref="IDocumentStore"/>. Multi-row writes are
/// single SaveChanges calls — one database transaction, the frozen guarantee
/// that a mid-loop failure cannot leave a half-written document graph.
/// </summary>
public sealed class DocumentStore(HsmDbContext db) : IDocumentStore
{
    public async Task AddAsync(Document document, CancellationToken ct = default)
    {
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<Document>> ListAsync(
        DocumentListFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Documents.AsNoTracking()
            .Where(d => d.CreatedBy == filter.CreatedBy && d.DeletedAt == null);

        // Frozen: the entity filter applies only when BOTH parts are present.
        if (!string.IsNullOrEmpty(filter.EntityId) && !string.IsNullOrEmpty(filter.EntityType))
        {
            query = query.Where(d => d.EntityId == filter.EntityId && d.EntityType == filter.EntityType);
        }

        if (filter.Type is not null)
        {
            query = query.Where(d => d.Type == filter.Type);
        }

        if (filter.Status is not null)
        {
            query = query.Where(d => d.Status == filter.Status);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new PagedResult<Document>(items, page, pageSize, total);
    }

    public async Task<Document?> FindWithVersionsAsync(Guid id, Guid createdBy, CancellationToken ct = default)
        => await db.Documents.AsNoTracking()
            .Include(d => d.Versions.OrderBy(v => v.Version))
            .ThenInclude(v => v.Storage)
            .FirstOrDefaultAsync(
                d => d.Id == id && d.CreatedBy == createdBy && d.DeletedAt == null, ct);

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
        => await db.Documents.Where(d => d.Id == id).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(d => d.DeletedAt, DateTimeOffset.UtcNow)
                .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow),
            ct);

    public async Task SetStatusAsync(Guid id, string status, CancellationToken ct = default)
        => await db.Documents.Where(d => d.Id == id).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow),
            ct);

    public async Task<int> AddGeneratedVersionAsync(GeneratedVersionRecord record, CancellationToken ct = default)
    {
        // Frozen: COALESCE(MAX(version),0)+1 under a write lock. Here the
        // unique (DocumentId, Version) index is the backstop — a concurrent
        // insert fails and the job's retry recomputes.
        var next = (await db.DocumentVersions
            .Where(v => v.DocumentId == record.DocumentId)
            .MaxAsync(v => (int?)v.Version, ct) ?? 0) + 1;

        var now = DateTimeOffset.UtcNow;
        db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            Version = next,
            Filename = record.Filename,
            MimeType = record.MimeType,
            Size = record.Size,
            DocumentId = record.DocumentId,
            CreatedAt = now,
            Storage = new DocumentStorageObject
            {
                Id = record.FileId,
                Path = record.Key,
                Bucket = record.Bucket,
                CreatedAt = now,
                UpdatedAt = now,
            },
            Generated = new DocumentGenerated
            {
                Id = Guid.NewGuid(),
                TemplateName = record.TemplateName,
                DataJson = record.DataJson,
                CreatedAt = now,
            },
        });

        // Frozen: a link plus entity fields on the document row, only when
        // both parts are present (truthy in the frozen worker).
        if (!string.IsNullOrEmpty(record.EntityId) && !string.IsNullOrEmpty(record.EntityType))
        {
            db.DocumentLinks.Add(new DocumentLink
            {
                Id = Guid.NewGuid(),
                DocumentId = record.DocumentId,
                EntityId = record.EntityId,
                EntityType = record.EntityType,
            });
            var document = await db.Documents.FirstAsync(d => d.Id == record.DocumentId, ct);
            document.EntityId = record.EntityId;
            document.EntityType = record.EntityType;
            document.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return next;
    }

    public async Task<IReadOnlyList<Guid>> AddUploadedDocumentsAsync(
        IReadOnlyList<UploadedDocumentRecord> records, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        foreach (var record in records)
        {
            var document = new Document
            {
                Id = Guid.NewGuid(),
                Title = record.Title,
                Type = DocumentTypes.Uploaded,
                Status = DocumentStatuses.Completed,
                Source = DocumentSources.Manual,
                CreatedBy = record.CreatedBy,
                CreatedAt = now,
                UpdatedAt = now,
                Versions =
                [
                    new DocumentVersion
                    {
                        Id = Guid.NewGuid(),
                        Version = 1,
                        Filename = record.Filename,
                        MimeType = record.MimeType,
                        Size = record.Size,
                        CreatedAt = now,
                        Storage = new DocumentStorageObject
                        {
                            Id = record.FileId,
                            Path = record.Key,
                            Bucket = record.Bucket,
                            CreatedAt = now,
                            UpdatedAt = now,
                        },
                    },
                ],
            };
            if (!string.IsNullOrEmpty(record.EntityId) && !string.IsNullOrEmpty(record.EntityType))
            {
                document.Links.Add(new DocumentLink
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    EntityId = record.EntityId,
                    EntityType = record.EntityType,
                });
            }

            db.Documents.Add(document);
            ids.Add(document.Id);
        }

        await db.SaveChangesAsync(ct);
        return ids;
    }
}
