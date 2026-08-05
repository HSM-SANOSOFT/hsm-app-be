using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Hsm.Application.Ports;

namespace Hsm.Infrastructure.Storage;

/// <summary>
/// S3-compatible adapter for <see cref="IObjectStorage"/>. The endpoint,
/// credentials, and bucket come from configuration — RustFS in dev/CI, any
/// S3-compatible store in production. SDK exceptions are translated to the
/// port's typed failures at this boundary. Presigning can use a second
/// client bound to an externally reachable endpoint (STRG_S3_HOST_EXTERNAL)
/// so minted URLs resolve outside the container network; when none is
/// configured the main client signs.
/// </summary>
public sealed class S3ObjectStorage(IAmazonS3 client, string bucketName, IAmazonS3? presignClient = null)
    : IObjectStorage
{
    /// <summary>Payloads strictly larger than this are chunked via multipart upload.</summary>
    public const long MultipartThreshold = 8 * 1024 * 1024;

    /// <summary>Multipart part size (the S3 minimum for non-final parts).</summary>
    public const long PartSize = 5 * 1024 * 1024;

    /// <summary>Default presign expiry: 900 seconds.</summary>
    public static readonly TimeSpan DefaultPresignExpiry = TimeSpan.FromSeconds(900);

    public async Task<ObjectPutResult> PutAsync(
        string key, Stream content, string contentType, string? bucket = null, CancellationToken cancellationToken = default)
    {
        var target = bucket ?? bucketName;
        var length = content.CanSeek ? content.Length - content.Position : -1;
        try
        {
            if (length > MultipartThreshold)
            {
                return await PutMultipartAsync(target, key, content, contentType, length, cancellationToken)
                    .ConfigureAwait(false);
            }

            var response = await client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = target,
                    Key = key,
                    InputStream = content,
                    ContentType = contentType,
                    AutoCloseStream = false,
                },
                cancellationToken).ConfigureAwait(false);
            return new ObjectPutResult(response.ETag, response.VersionId);
        }
        catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            throw Translate("put", target, key, ex);
        }
    }

    public async Task<Stream> GetAsync(string key, string? bucket = null, CancellationToken cancellationToken = default)
    {
        var target = bucket ?? bucketName;
        try
        {
            var response = await client.GetObjectAsync(target, key, cancellationToken).ConfigureAwait(false);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ObjectNotFoundException($"No object at '{key}' in bucket '{target}'.", ex);
        }
        catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            throw Translate("get", target, key, ex);
        }
    }

    public async Task<Stream> GetVersionAsync(
        string key, string versionId, string? bucket = null, CancellationToken cancellationToken = default)
    {
        var target = bucket ?? bucketName;
        try
        {
            var response = await client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = target,
                    Key = key,
                    VersionId = versionId,
                },
                cancellationToken).ConfigureAwait(false);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ObjectNotFoundException(
                $"No object at '{key}' (version '{versionId}') in bucket '{target}'.", ex);
        }
        catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            throw Translate("get-version", target, key, ex);
        }
    }

    public async Task DeleteAsync(string key, string? bucket = null, CancellationToken cancellationToken = default)
    {
        var target = bucket ?? bucketName;
        try
        {
            await client.DeleteObjectAsync(target, key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            throw Translate("delete", target, key, ex);
        }
    }

    public async Task<Uri> PresignGetAsync(
        string key,
        TimeSpan? expiresIn = null,
        string? contentDisposition = null,
        string? bucket = null,
        CancellationToken cancellationToken = default)
    {
        var target = bucket ?? bucketName;
        var signer = presignClient ?? client;
        try
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = target,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(expiresIn ?? DefaultPresignExpiry),
            };
            if (signer.Config.ServiceURL is { } serviceUrl
                && serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                request.Protocol = Protocol.HTTP;
            }

            if (contentDisposition is not null)
            {
                request.ResponseHeaderOverrides.ContentDisposition = contentDisposition;
            }

            var url = await signer.GetPreSignedURLAsync(request).ConfigureAwait(false);
            return new Uri(url);
        }
        catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException)
        {
            throw Translate("presign", target, key, ex);
        }
    }

    /// <summary>
    /// Explicit low-level multipart (initiate/part/complete) so chunking is
    /// deterministic and provable: a completed multipart object's ETag ends
    /// in "-{partCount}", which the RustFS validation test asserts.
    /// </summary>
    private async Task<ObjectPutResult> PutMultipartAsync(
        string bucket, string key, Stream content, string contentType, long length, CancellationToken cancellationToken)
    {
        var initiated = await client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest
            {
                BucketName = bucket,
                Key = key,
                ContentType = contentType,
            },
            cancellationToken).ConfigureAwait(false);

        try
        {
            var partETags = new List<PartETag>();
            var remaining = length;
            for (var partNumber = 1; remaining > 0; partNumber++)
            {
                var size = Math.Min(PartSize, remaining);
                var part = await client.UploadPartAsync(
                    new UploadPartRequest
                    {
                        BucketName = bucket,
                        Key = key,
                        UploadId = initiated.UploadId,
                        PartNumber = partNumber,
                        PartSize = size,
                        InputStream = content,
                        IsLastPart = remaining == size,
                    },
                    cancellationToken).ConfigureAwait(false);
                partETags.Add(new PartETag(partNumber, part.ETag));
                remaining -= size;
            }

            var completed = await client.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest
                {
                    BucketName = bucket,
                    Key = key,
                    UploadId = initiated.UploadId,
                    PartETags = partETags,
                },
                cancellationToken).ConfigureAwait(false);
            return new ObjectPutResult(completed.ETag, completed.VersionId);
        }
        catch
        {
            await AbortQuietlyAsync(bucket, key, initiated.UploadId).ConfigureAwait(false);
            throw;
        }
    }

    private async Task AbortQuietlyAsync(string bucket, string key, string uploadId)
    {
        try
        {
            await client.AbortMultipartUploadAsync(bucket, key, uploadId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException)
        {
            // Abort is best effort — the original failure is the signal.
        }
    }

    private static ObjectStorageUnavailableException Translate(string operation, string bucket, string key, Exception ex)
        => new ObjectStorageUnavailableException(
            $"Object storage {operation} failed for '{key}' in bucket '{bucket}'.", ex);
}
