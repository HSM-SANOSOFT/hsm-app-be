using System.Text;
using Amazon.S3;
using Hsm.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

public sealed class ObjectStorageTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _provider = TestServices.Build();

        // The adapter assumes the bucket exists (bucket provisioning is an
        // operational concern); create it here for the test run.
        var s3 = _provider.GetRequiredService<IAmazonS3>();
        var bucket = TestServices.Configuration["Storage:S3:Bucket"]!;
        var buckets = await s3.ListBucketsAsync();
        if (buckets.Buckets is null || !buckets.Buckets.Any(b => b.BucketName == bucket))
        {
            await s3.PutBucketAsync(bucket);
        }
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Object_round_trips_through_the_port()
    {
        var storage = _provider.GetRequiredService<IObjectStorage>();
        var key = $"proving/{Guid.NewGuid()}.txt";
        var payload = "object storage round trip"u8.ToArray();

        using (var content = new MemoryStream(payload))
        {
            await storage.PutAsync(key, content, "text/plain");
        }

        await using (var fetched = await storage.GetAsync(key))
        {
            using var reader = new StreamReader(fetched, Encoding.UTF8);
            Assert.Equal("object storage round trip", await reader.ReadToEndAsync());
        }

        await storage.DeleteAsync(key);
        await Assert.ThrowsAsync<ObjectNotFoundException>(() => storage.GetAsync(key));
    }

    [Fact]
    public async Task Unreachable_endpoint_surfaces_the_typed_failure()
    {
        // Same binary, different configuration — pointed at a dead endpoint,
        // the port must throw its own typed failure, not an SDK exception.
        using var provider = TestServices.Build(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:S3:Endpoint"] = "http://127.0.0.1:1",
            }));

        var storage = provider.GetRequiredService<IObjectStorage>();
        using var content = new MemoryStream("unreachable"u8.ToArray());

        await Assert.ThrowsAsync<ObjectStorageUnavailableException>(
            () => storage.PutAsync("proving/unreachable.txt", content, "text/plain"));
    }
}
