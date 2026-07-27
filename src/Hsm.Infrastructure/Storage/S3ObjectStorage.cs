using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Hsm.Application.Ports;

namespace Hsm.Infrastructure.Storage;

/// <summary>
/// S3-compatible adapter for <see cref="IObjectStorage"/>. The endpoint,
/// credentials, and bucket come from configuration — RustFS in dev/CI, any
/// S3-compatible store in production. SDK exceptions are translated to the
/// port's typed failures at this boundary.
/// </summary>
public sealed class S3ObjectStorage(IAmazonS3 client, string bucketName) : IObjectStorage
{
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    InputStream = content,
                    ContentType = contentType,
                    AutoCloseStream = false,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            throw Translate("put", key, ex);
        }
    }

    public async Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.GetObjectAsync(bucketName, key, cancellationToken).ConfigureAwait(false);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ObjectNotFoundException($"No object at '{key}' in bucket '{bucketName}'.", ex);
        }
        catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            throw Translate("get", key, ex);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.DeleteObjectAsync(bucketName, key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            throw Translate("delete", key, ex);
        }
    }

    private ObjectStorageUnavailableException Translate(string operation, string key, Exception ex)
        => new ObjectStorageUnavailableException(
            $"Object storage {operation} failed for '{key}' in bucket '{bucketName}'.", ex);
}
