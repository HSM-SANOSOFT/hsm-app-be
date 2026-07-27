namespace Hsm.Application.Ports;

/// <summary>
/// Blob storage port. The application layer sees roles, never products: the
/// adapter binds to any S3-compatible endpoint purely through configuration
/// (RustFS today; SeaweedFS or Ceph would be a config change, not a code
/// change). Failures surface as <see cref="ObjectStorageException"/>
/// subtypes — infrastructure exception types must not cross this boundary.
/// </summary>
public interface IObjectStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <exception cref="ObjectNotFoundException">No object exists at <paramref name="key"/>.</exception>
    Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

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
