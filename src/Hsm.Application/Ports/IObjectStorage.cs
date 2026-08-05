namespace Hsm.Application.Ports;

/// <summary>
/// Blob storage port. The application layer sees roles, never products: the
/// adapter binds to any S3-compatible endpoint purely through configuration
/// (RustFS today; SeaweedFS or Ceph would be a config change, not a code
/// change). Failures surface as <see cref="ObjectStorageException"/>
/// subtypes — infrastructure exception types must not cross this boundary.
/// Every method takes an optional bucket because the document contract
/// carries bucket names in request payloads; null means the configured
/// default bucket.
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// Stores an object. Large seekable payloads are chunked via multipart
    /// upload (see the adapter's threshold); the result carries the ETag and,
    /// on versioned buckets, the created version id.
    /// </summary>
    Task<ObjectPutResult> PutAsync(
        string key, Stream content, string contentType, string? bucket = null, CancellationToken cancellationToken = default);

    /// <exception cref="ObjectNotFoundException">No object exists at <paramref name="key"/>.</exception>
    Task<Stream> GetAsync(string key, string? bucket = null, CancellationToken cancellationToken = default);

    /// <summary>Retrieves an explicit object version from a versioned bucket.</summary>
    /// <exception cref="ObjectNotFoundException">No such key or version.</exception>
    Task<Stream> GetVersionAsync(
        string key, string versionId, string? bucket = null, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, string? bucket = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a time-limited presigned GET URL. Signing is a local
    /// operation — no round-trip, and no existence check: URLs are minted
    /// even for absent keys, and the grant fails on use. Default expiry is
    /// 900 seconds; inline disposition only when explicitly requested.
    /// </summary>
    Task<Uri> PresignGetAsync(
        string key,
        TimeSpan? expiresIn = null,
        string? contentDisposition = null,
        string? bucket = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a put: ETag and, on versioned buckets, the version id.</summary>
public sealed record ObjectPutResult(string? ETag, string? VersionId);

public class ObjectStorageException : Exception
{
    public ObjectStorageException()
    {
    }

    public ObjectStorageException(string message)
        : base(message)
    {
    }

    public ObjectStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The storage endpoint could not be reached or refused the operation.</summary>
public class ObjectStorageUnavailableException : ObjectStorageException
{
    public ObjectStorageUnavailableException()
    {
    }

    public ObjectStorageUnavailableException(string message)
        : base(message)
    {
    }

    public ObjectStorageUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>No object exists at the requested key.</summary>
public class ObjectNotFoundException : ObjectStorageException
{
    public ObjectNotFoundException()
    {
    }

    public ObjectNotFoundException(string message)
        : base(message)
    {
    }

    public ObjectNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
