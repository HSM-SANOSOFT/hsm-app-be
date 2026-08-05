using System.Text.Json.Nodes;
using Hsm.Domain.Docs;

namespace Hsm.Api.Documents;

/// <summary>
/// The wire shape of a stored document. <see cref="Type"/>/<see cref="Status"/>
/// are direct copies of the <see cref="Document"/> row, not a derived value:
/// <c>DocumentStore.AddUploadedDocumentsAsync</c> stamps
/// <c>Type=UPLOADED</c>/<c>Status=COMPLETED</c> at write time,
/// <c>GenerateDocumentHandler</c> stamps <c>Type=GENERATED</c>/<c>Status=PENDING</c>,
/// and <c>RenderDocumentHandler</c> (via <c>IDocumentStore.SetStatusAsync</c>) is
/// the only place a generated document's status ever moves off PENDING — there
/// is no separate aggregation rule for this resource to duplicate here, unlike
/// <c>EmailResource.SentCount</c> in Task 6.
/// </summary>
public sealed record DocumentResource(
    Guid Id, string Title, string? Description, string Type, string Status,
    string? EntityId, string? EntityType, Guid CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static DocumentResource From(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentResource(
            document.Id,
            document.Title,
            document.Description,
            document.Type,
            document.Status,
            document.EntityId,
            document.EntityType,
            // Every write path (GenerateDocumentHandler, UploadDocumentsHandler)
            // stamps CreatedBy from the acting principal; the column is
            // nullable only because the schema allows it, so this is a
            // safe default, not a fabricated value.
            document.CreatedBy.GetValueOrDefault(),
            document.CreatedAt,
            document.UpdatedAt);
    }
}

public sealed record DocumentDetailResource(
    Guid Id, string Title, string? Description, string Type, string Status,
    string? EntityId, string? EntityType, Guid CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<DocumentVersionResource> Versions)
{
    public static DocumentDetailResource From(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var summary = DocumentResource.From(document);
        return new DocumentDetailResource(
            summary.Id, summary.Title, summary.Description, summary.Type, summary.Status,
            summary.EntityId, summary.EntityType, summary.CreatedBy, summary.CreatedAt, summary.UpdatedAt,
            // DocumentStore.FindWithVersionsAsync's own Include orders by
            // Version ascending; repeated here so this resource stays correct
            // even if a future caller of Document.Versions does not.
            [.. document.Versions.OrderBy(v => v.Version).Select(DocumentVersionResource.From)]);
    }
}

/// <summary><see cref="Key"/> is the version's object-store key
/// (<see cref="DocumentStorageObject.Path"/>) — null only for a generated
/// version whose render job has not produced a storage object yet.</summary>
public sealed record DocumentVersionResource(
    Guid Id, int Version, string? Key, long? Size, string? ContentType, DateTimeOffset CreatedAt)
{
    public static DocumentVersionResource From(DocumentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new DocumentVersionResource(
            version.Id, version.Version, version.Storage?.Path, version.Size, version.MimeType, version.CreatedAt);
    }
}

public sealed record UploadedFileResource(string FileId, string Filename, string Key);

public sealed record UploadedItemResource(string Bucket, IReadOnlyList<UploadedFileResource> Files);

public sealed record UploadResultResource(
    IReadOnlyList<UploadedItemResource> Items, IReadOnlyList<Guid> DocumentIds);

public sealed record PresignRequest(
    IReadOnlyList<PresignItemRequest> Items, string? ContentDisposition, int? ExpiresInSeconds);

public sealed record PresignItemRequest(string Bucket, IReadOnlyList<PresignFileRequest> Files);

public sealed record PresignFileRequest(string FolderName, string FileId);

public sealed record PresignedFileResource(string FileId, string Key, string Url);

public sealed record PresignedItemResource(string Bucket, IReadOnlyList<PresignedFileResource> Files);

public sealed record DocumentUrlResource(string Url);

public sealed record AcceptedDocumentResponse(Guid Id, string JobId);

/// <summary>
/// The generateDocument body shape, promoted from DocsEndpoints'
/// private record to a public request record — same fields, same semantics
/// (<see cref="Data"/> is a raw JSON object, serialized back to a string for
/// the command). Not part of the brief's resource list because it maps
/// 1:1 onto <c>GenerateDocumentCommand</c>'s own parameters; kept here
/// alongside every other request/response record for this module.
/// </summary>
public sealed record GenerateDocumentRequest(
    string TemplateIdentifier,
    JsonObject? Data,
    string Title,
    string? Description,
    string? EntityId,
    string? EntityType);
