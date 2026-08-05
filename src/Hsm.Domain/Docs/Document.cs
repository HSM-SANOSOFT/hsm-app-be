namespace Hsm.Domain.Docs;

/// <summary>
/// A stored document: metadata only — the binary lives in the blob store,
/// never in a database column (proven by schema-inspection test).
/// Soft-deleted via <see cref="DeletedAt"/>; the list/get surface filters
/// deleted rows out.
/// </summary>
public class Document
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>One of <see cref="DocumentTypes"/>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>One of <see cref="DocumentStatuses"/>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>One of <see cref="DocumentSources"/>.</summary>
    public string Source { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    public string? EntityType { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<DocumentVersion> Versions { get; set; } = [];

    public List<DocumentLink> Links { get; set; } = [];

    public List<DocumentAuditLog> Audits { get; set; } = [];
}

/// <summary>
/// One version of a document: versions are relational — each points at its
/// own blob-store object under a distinct key; S3 object versioning is not
/// how document history is modelled. Unique per (document, version).
/// </summary>
public class DocumentVersion
{
    public Guid Id { get; set; }

    public int Version { get; set; }

    public string? Filename { get; set; }

    public string? MimeType { get; set; }

    public long? Size { get; set; }

    public Guid DocumentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DocumentStorageObject? Storage { get; set; }

    public DocumentGenerated? Generated { get; set; }
}

/// <summary>
/// The blob-store coordinates of one version: the id IS the blob-store file
/// id (the key's final segment), assigned at upload time — not
/// database-generated.
/// </summary>
public class DocumentStorageObject
{
    public Guid Id { get; set; }

    /// <summary>The object key, e.g. "hcu-013-a/&lt;uuid&gt;".</summary>
    public string Path { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string? Region { get; set; }

    public string? ETag { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid VersionId { get; set; }
}

/// <summary>
/// A link from a document to another record. Deleting a linked document
/// soft-deletes the document and leaves the link rows in place — neither
/// rejected nor cascaded (the document row survives, so links stay
/// resolvable).
/// </summary>
public class DocumentLink
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string EntityId { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;
}

/// <summary>
/// Generation provenance of one version: the template name and the raw
/// substitution data (jsonb) it was rendered with.
/// </summary>
public class DocumentGenerated
{
    public Guid Id { get; set; }

    public string TemplateName { get; set; } = string.Empty;

    public string DataJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid VersionId { get; set; }
}

/// <summary>
/// Audit row. This table exists and nothing writes to it: present, empty,
/// ready for rows.
/// </summary>
public class DocumentAuditLog
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string Action { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
