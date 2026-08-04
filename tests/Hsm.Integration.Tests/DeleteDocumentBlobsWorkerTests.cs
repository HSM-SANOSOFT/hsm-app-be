using Amazon.S3;
using Hsm.Application.Abstractions;
using Hsm.Application.Docs.Commands.DeleteDocument;
using Hsm.Application.Docs.Commands.DeleteDocumentBlobs;
using Hsm.Application.Ports;
using Hsm.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hsm.Integration.Tests;

/// <summary>
/// Task 7 review fix: <c>DeleteDocumentHandler</c> no longer deletes S3/RustFS
/// blobs itself — it only captures their coordinates, and the caller enqueues
/// <see cref="DeleteDocumentBlobsCommand"/> strictly after the deleting
/// transaction has committed (see that handler's own doc comment: a
/// non-transactional S3 delete run inside a bulk-delete's ambient transaction
/// has no rollback if a LATER id in the same batch fails). The
/// <c>Hsm.Api.Tests</c> suite never consumes jobs (every module's factory
/// leaves <c>ConsumesJobs</c> false), so it can only prove the job is
/// scheduled correctly — not that the job actually does its job once a worker
/// picks it up. This test proves that half, the same way
/// <see cref="WorkerEndToEndTests"/> proves it for
/// <c>DispatchEmailBatchCommand</c>: no HTTP host, the real queue, the real
/// worker composition.
/// </summary>
public sealed class DeleteDocumentBlobsWorkerTests : IAsyncLifetime
{
    private const string Bucket = "hsm-integration-tests";

    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _provider = BuildWorker();
        await TestServices.RecreateAsync(_provider);

        // The adapter assumes the bucket exists (bucket provisioning is an
        // operational concern); create it here for the test run, same as
        // ObjectStorageTests.
        var s3 = _provider.GetRequiredService<IAmazonS3>();
        var buckets = await s3.ListBucketsAsync();
        if (buckets.Buckets is null || !buckets.Buckets.Any(b => b.BucketName == Bucket))
        {
            await s3.PutBucketAsync(Bucket);
        }
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Worker_deletes_the_documents_blob_once_the_job_runs()
    {
        var key = $"docs-worker-test/{Guid.NewGuid()}.txt";
        var storage = _provider.GetRequiredService<IObjectStorage>();
        using (var content = new MemoryStream("delete me"u8.ToArray()))
        {
            await storage.PutAsync(key, content, "text/plain", Bucket);
        }

        // Confirms the object is really there before the job runs, so
        // "unreachable" later can't be explained by it never having existed.
        await using (var before = await storage.GetAsync(key, Bucket))
        {
            using var reader = new StreamReader(before);
            Assert.Equal("delete me", await reader.ReadToEndAsync());
        }

        // Enqueued exactly as DocumentEndpoints would enqueue it: through the
        // real IJobQueue, on the real 'docs' queue, carrying the caller's actor.
        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AmbientPrincipal>().Set(
                new RequestActor(Guid.NewGuid().ToString(), ["nurse"], OnboardingCompleted: true));
            await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(
                new DeleteDocumentBlobsCommand(Guid.NewGuid(), [new DocumentBlobRef(key, Bucket)]));
        }

        await RunConsumerUntilAsync(async () =>
        {
            try
            {
                await (await storage.GetAsync(key, Bucket)).DisposeAsync();
                return false;
            }
            catch (ObjectNotFoundException)
            {
                return true;
            }
        });

        await Assert.ThrowsAsync<ObjectNotFoundException>(() => storage.GetAsync(key, Bucket));
    }

    /// <summary>
    /// Runs the worker's hosted services — the consume loop and the delayed
    /// pump, exactly as <c>Hsm.Worker</c> runs them — until
    /// <paramref name="done"/> holds, then shuts them down.
    /// </summary>
    private async Task RunConsumerUntilAsync(Func<Task<bool>> done)
    {
        var hosted = _provider.GetServices<IHostedService>().ToList();
        Assert.Contains(hosted, service => service is JobConsumerService);

        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }

        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await done())
                {
                    return;
                }

                await Task.Delay(50);
            }

            Assert.Fail("Timed out waiting for the worker to delete the blob.");
        }
        finally
        {
            foreach (var service in hosted)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// The worker host's composition, minus the OpenTelemetry exporter: the
    /// same three calls <c>Hsm.Worker/Program.cs</c> makes, against a database
    /// and a queue namespace dedicated to this suite.
    /// </summary>
    private static ServiceProvider BuildWorker() =>
        TestServices.Build(
            builder => builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HsmDb"] = DedicatedDatabase("hsm_delete_document_blobs_worker_test"),
                ["Jobs:KeyPrefix"] = $"hsmtest:{Guid.NewGuid():N}",
                ["Docs:RetryBaseDelayMs"] = "50",
            }),
            services =>
            {
                services.AddHsmPipeline();
                services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<AmbientPrincipal>());
                services.AddHsmJobProcessing();
            });

    private static string DedicatedDatabase(string name) => string.Join(
        ';',
        TestServices.Configuration["ConnectionStrings:HsmDb"]!
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
            .Append($"Database={name}"));
}
