using Hsm.Contracts;
using Hsm.Domain.Docs;

namespace Hsm.Application.Docs;

/// <summary>List filter (frozen ListDocumentsQueryDto + createdBy scoping).</summary>
public sealed record DocumentListFilter(
    Guid CreatedBy,
    string? EntityId,
    string? EntityType,
    string? Type,
    string? Status);

/// <summary>
/// Everything one generated version persists in a single transaction (frozen
/// worker: version max+1, storage object, generation provenance, optional
/// link plus entity fields on the document row).
/// </summary>
public sealed record GeneratedVersionRecord(
    Guid DocumentId,
    string Filename,
    string MimeType,
    long Size,
    Guid FileId,
    string Key,
    string Bucket,
    string TemplateName,
    string DataJson,
    string? EntityId,
    string? EntityType);

/// <summary>
/// One uploaded file's document graph (frozen uploadDocuments transaction:
/// document + version 1 + storage object + optional link).
/// </summary>
public sealed record UploadedDocumentRecord(
    string Title,
    string? Filename,
    string? MimeType,
    long? Size,
    Guid FileId,
    string Key,
    string Bucket,
    Guid? CreatedBy,
    string? EntityId,
    string? EntityType);

/// <summary>Persistence port for the documents aggregate.</summary>
public interface IDocumentStore
{
    Task AddAsync(Document document, CancellationToken ct = default);

    /// <summary>Frozen list: createdBy scope, deleted excluded, createdAt DESC.</summary>
    Task<PagedResult<Document>> ListAsync(
        DocumentListFilter filter, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Frozen findOne({ id, createdBy }, relations: versions.storage):
    /// deleted rows excluded, versions with their storage objects loaded.
    /// </summary>
    Task<Document?> FindWithVersionsAsync(Guid id, Guid createdBy, CancellationToken ct = default);

    /// <summary>Frozen softDelete: stamps deletedAt; every other row survives.</summary>
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);

    Task SetStatusAsync(Guid id, string status, CancellationToken ct = default);

    /// <summary>
    /// One transaction: next version number (unique per document), storage
    /// object, generation provenance, optional link + entity fields on the
    /// document row. Returns the created version number.
    /// </summary>
    Task<int> AddGeneratedVersionAsync(GeneratedVersionRecord record, CancellationToken ct = default);

    /// <summary>
    /// One transaction for the whole upload batch (frozen: a mid-loop failure
    /// must not leave a half-written document graph). Returns the created
    /// document ids, in order.
    /// </summary>
    Task<IReadOnlyList<Guid>> AddUploadedDocumentsAsync(
        IReadOnlyList<UploadedDocumentRecord> records, CancellationToken ct = default);
}
