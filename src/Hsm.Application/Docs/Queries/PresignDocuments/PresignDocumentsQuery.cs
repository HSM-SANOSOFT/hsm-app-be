using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Queries.PresignDocuments;

/// <summary>
/// POST /v1/docs/url.
/// A QUERY, not a command: signing is local (HMAC over the request), no round
/// trip, no persistence — nothing here mutates state. Authenticated only.
/// </summary>
public sealed record PresignDocumentsQuery(
    IReadOnlyList<PresignItem> Items,
    string? ContentDisposition,
    int? ExpiresInSeconds) : IQuery<IReadOnlyList<PresignedItem>>;

public sealed record PresignFileRef(string FolderName, string FileId);

public sealed record PresignItem(string Bucket, IReadOnlyList<PresignFileRef> Files);

public sealed record PresignedFile(string FileId, string Key, string Url);

public sealed record PresignedItem(string Bucket, IReadOnlyList<PresignedFile> Files);
