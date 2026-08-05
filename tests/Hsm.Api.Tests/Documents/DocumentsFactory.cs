using Amazon.S3;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Api.Tests.Documents;

/// <summary>
/// Boots against real RustFS, the same way <c>AdminScreensFactory</c> does for
/// the Blazor shell suite: Upload/Presign/GetUrl/Delete all round-trip through
/// <see cref="Hsm.Application.Ports.IObjectStorage"/>, so this module cannot
/// be exercised against an in-memory fake.
/// </summary>
public class DocumentsFactory : ApiFactory
{
    public const string Bucket = "hsm-api-tests-documents";

    protected override string DatabaseName => "hsm_api_tests_documents";

    protected override void ConfigureModule(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "Storage:S3:Endpoint",
            Environment.GetEnvironmentVariable("Storage__S3__Endpoint") ?? "http://rustfs:9000");
        builder.UseSetting(
            "Storage:S3:AccessKey",
            Environment.GetEnvironmentVariable("Storage__S3__AccessKey") ?? "rustfs_user");
        builder.UseSetting(
            "Storage:S3:SecretKey",
            Environment.GetEnvironmentVariable("Storage__S3__SecretKey") ?? "rustfs_password");
        builder.UseSetting("Storage:S3:ForcePathStyle", "true");
        builder.UseSetting("Docs:Bucket", Bucket);
    }

    protected override async Task OnSchemaCreatedAsync()
    {
        var s3 = Services.GetRequiredService<IAmazonS3>();
        var buckets = await s3.ListBucketsAsync();
        if (buckets.Buckets is null || !buckets.Buckets.Any(b => b.BucketName == Bucket))
        {
            await s3.PutBucketAsync(Bucket);
        }
    }
}
