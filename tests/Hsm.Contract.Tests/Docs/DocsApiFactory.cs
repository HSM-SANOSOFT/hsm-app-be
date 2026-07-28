using Amazon.S3;
using Hsm.Domain.Templates;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Docs;

/// <summary>
/// The U15 docs suite host (hsm_docs_test) — runs against the real RustFS as
/// well: the frozen /v1/docs surface presigns, uploads, and deletes actual
/// blobs, so a stub store would prove nothing. Uses a dedicated bucket.
/// </summary>
public sealed class DocsApiFactory : ContractApiFactory
{
    public const string Bucket = "hsm-docs-contract-tests";

    protected override string DatabaseName => "hsm_docs_test";

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
        // Deployment-tunable queue timings, tightened so the suite does not
        // sleep through the frozen 1s enqueue delay on every generation.
        builder.UseSetting("Docs:InitialDelayMs", "25");
        builder.UseSetting("Docs:RetryBaseDelayMs", "50");
    }

    /// <summary>
    /// The adapter assumes the bucket exists (provisioning is an operational
    /// concern); create it for the test run.
    /// </summary>
    protected override async Task OnSchemaCreatedAsync()
    {
        var s3 = Services.GetRequiredService<IAmazonS3>();
        var buckets = await s3.ListBucketsAsync();
        if (buckets.Buckets is null || !buckets.Buckets.Any(b => b.BucketName == Bucket))
        {
            await s3.PutBucketAsync(Bucket);
        }
    }

    /// <summary>
    /// Seeds a DOCS-category template (with its BASE) directly and returns
    /// the child template's name — the shape the generation job consumes.
    /// </summary>
    public async Task<string> SeedDocsTemplateAsync(
        string name,
        string documentCode = "HCU-001",
        string format = "PDF",
        bool isActive = true,
        string schemaJson = "{}",
        string content = "<p>Hola {{patientName}}</p>")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var baseTemplate = new Template
        {
            Id = Guid.NewGuid(),
            Category = TemplateCategories.Base,
            Name = $"{name}_base",
            IsActive = true,
            SchemaJson = "{}",
            Content = "<html><body>{{{body}}}</body></html>",
        };
        var template = new Template
        {
            Id = Guid.NewGuid(),
            Category = TemplateCategories.Docs,
            Name = name,
            IsActive = isActive,
            SchemaJson = schemaJson,
            Content = content,
            BaseTemplateId = baseTemplate.Id,
            Doc = new TemplateDoc
            {
                DocumentCode = documentCode,
                Format = format,
                Size = "A4",
                Orientation = "PORTRAIT",
            },
        };
        db.Templates.Add(baseTemplate);
        db.Templates.Add(template);
        await db.SaveChangesAsync();
        return name;
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> work)
    {
        using var scope = Services.CreateScope();
        await work(scope.ServiceProvider);
    }
}
