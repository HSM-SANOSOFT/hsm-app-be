using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Hsm.Application.Ports;
using Hsm.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

/// <summary>
/// U15 RustFS compatibility validation — run BEFORE the documents module
/// builds on these surfaces (plan execution note: "100% S3-compatible" is
/// usually 95%, and the missing 5% is always something you need). Proves,
/// against the real store at Storage:S3:Endpoint, the three surfaces the
/// origin document names: multipart upload, presigned URLs (grant and
/// expiry), and object versioning (explicit-version retrieval).
/// </summary>
public sealed class RustFsValidationTests : IAsyncLifetime
{
    private const string Bucket = "hsm-rustfs-validation";
    private const string VersionedBucket = "hsm-rustfs-validation-versioned";

    private ServiceProvider _provider = null!;
    private IAmazonS3 _s3 = null!;
    private IObjectStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _provider = TestServices.Build();
        _s3 = _provider.GetRequiredService<IAmazonS3>();
        _storage = _provider.GetRequiredService<IObjectStorage>();
        await EnsureBucketAsync(Bucket);
        await EnsureBucketAsync(VersionedBucket);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Multipart_upload_reassembles_byte_for_byte()
    {
        // 12 MiB: strictly above the 8 MiB threshold, chunked as 5+5+2 MiB.
        var payload = RandomPayload(12 * 1024 * 1024);
        var key = $"multipart/{Guid.NewGuid()}.bin";

        ObjectPutResult result;
        using (var content = new MemoryStream(payload))
        {
            result = await _storage.PutAsync(key, content, "application/octet-stream", Bucket);
        }

        // A completed multipart object's ETag ends in "-{partCount}" — this
        // is the proof that chunking actually happened on the wire.
        Assert.NotNull(result.ETag);
        Assert.EndsWith("-3", result.ETag.Trim('"'), StringComparison.Ordinal);

        await using var fetched = await _storage.GetAsync(key, Bucket);
        Assert.Equal(Sha256(payload), await Sha256Async(fetched));
    }

    [Fact]
    public async Task File_at_the_multipart_threshold_boundary_stays_single_part_and_round_trips()
    {
        // Exactly at the threshold: NOT chunked (multipart starts strictly
        // above), so the ETag has no part suffix.
        var payload = RandomPayload((int)S3ObjectStorage.MultipartThreshold);
        var key = $"multipart/{Guid.NewGuid()}-boundary.bin";

        ObjectPutResult result;
        using (var content = new MemoryStream(payload))
        {
            result = await _storage.PutAsync(key, content, "application/octet-stream", Bucket);
        }

        Assert.NotNull(result.ETag);
        Assert.DoesNotContain("-", result.ETag.Trim('"'), StringComparison.Ordinal);

        await using var fetched = await _storage.GetAsync(key, Bucket);
        Assert.Equal(Sha256(payload), await Sha256Async(fetched));
    }

    [Fact]
    public async Task Zero_byte_object_round_trips()
    {
        var key = $"edge/{Guid.NewGuid()}-empty.bin";
        using (var content = new MemoryStream([]))
        {
            await _storage.PutAsync(key, content, "application/octet-stream", Bucket);
        }

        await using var fetched = await _storage.GetAsync(key, Bucket);
        using var buffer = new MemoryStream();
        await fetched.CopyToAsync(buffer);
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public async Task Presigned_url_grants_time_limited_access()
    {
        var payload = "presigned grant works"u8.ToArray();
        var key = $"presigned/{Guid.NewGuid()}.txt";
        using (var content = new MemoryStream(payload))
        {
            await _storage.PutAsync(key, content, "text/plain", Bucket);
        }

        var url = await _storage.PresignGetAsync(
            key, TimeSpan.FromMinutes(5), contentDisposition: "inline", bucket: Bucket);

        using var http = new HttpClient();
        using var response = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.ToString());
    }

    [Fact]
    public async Task Presigned_url_after_expiry_is_rejected()
    {
        var key = $"presigned/{Guid.NewGuid()}-expiring.txt";
        using (var content = new MemoryStream("short-lived"u8.ToArray()))
        {
            await _storage.PutAsync(key, content, "text/plain", Bucket);
        }

        var url = await _storage.PresignGetAsync(key, TimeSpan.FromSeconds(1), bucket: Bucket);
        await Task.Delay(TimeSpan.FromSeconds(3));

        using var http = new HttpClient();
        using var response = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Object_versioning_preserves_both_versions_and_explicit_retrieval_is_correct()
    {
        await _s3.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = VersionedBucket,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled },
        });

        var key = $"versioned/{Guid.NewGuid()}.txt";
        var first = "version one"u8.ToArray();
        var second = "version two"u8.ToArray();

        ObjectPutResult firstPut;
        ObjectPutResult secondPut;
        using (var content = new MemoryStream(first))
        {
            firstPut = await _storage.PutAsync(key, content, "text/plain", VersionedBucket);
        }

        using (var content = new MemoryStream(second))
        {
            secondPut = await _storage.PutAsync(key, content, "text/plain", VersionedBucket);
        }

        Assert.NotNull(firstPut.VersionId);
        Assert.NotNull(secondPut.VersionId);
        Assert.NotEqual(firstPut.VersionId, secondPut.VersionId);

        // Unversioned read → latest.
        await using (var latest = await _storage.GetAsync(key, VersionedBucket))
        {
            Assert.Equal(Sha256(second), await Sha256Async(latest));
        }

        // Explicit-version reads → each preserved version, byte for byte.
        await using (var v1 = await _storage.GetVersionAsync(key, firstPut.VersionId, VersionedBucket))
        {
            Assert.Equal(Sha256(first), await Sha256Async(v1));
        }

        await using (var v2 = await _storage.GetVersionAsync(key, secondPut.VersionId, VersionedBucket))
        {
            Assert.Equal(Sha256(second), await Sha256Async(v2));
        }
    }

    private async Task EnsureBucketAsync(string bucket)
    {
        var buckets = await _s3.ListBucketsAsync();
        if (buckets.Buckets is null || !buckets.Buckets.Any(b => b.BucketName == bucket))
        {
            await _s3.PutBucketAsync(bucket);
        }
    }

    private static byte[] RandomPayload(int size)
    {
        var payload = new byte[size];
        RandomNumberGenerator.Fill(payload);
        return payload;
    }

    private static string Sha256(byte[] payload) => Convert.ToHexString(SHA256.HashData(payload));

    private static async Task<string> Sha256Async(Stream stream)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream));
    }
}
