using Amazon.S3;
using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Hsm.Domain.Templates;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Docs;

/// <summary>
/// Boots the real host against the real dev-container PostgreSQL AND the real
/// RustFS — the frozen /v1/docs surface presigns, uploads, and deletes actual
/// blobs, so a stub store would prove nothing. Dedicated database
/// (hsm_docs_test) and bucket, sibling of the other suites' factories.
/// </summary>
public sealed class DocsApiFactory : WebApplicationFactory<Program>
{
    public const string Bucket = "hsm-docs-contract-tests";

    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool _schemaReady;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HsmDb", ConnectionString());
        builder.UseSetting("Auth:JwtAccessSecret", "contract_test_at_secret_0123456789abcdef");
        builder.UseSetting("Auth:JwtRefreshSecret", "contract_test_rt_secret_0123456789abcdef");
        builder.UseSetting("Auth:CsrfSecret", "contract_test_csrf_secret_0123456789abcdef");
        builder.UseSetting("Auth:Environment", "dev");
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

    private static string ConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__HsmDb")
            ?? "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=hsm";
        var withoutDatabase = configured
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
        return string.Join(';', withoutDatabase.Append("Database=hsm_docs_test"));
    }

    public async Task EnsureSchemaAsync()
    {
        if (_schemaReady)
        {
            return;
        }

        await SchemaGate.WaitAsync();
        try
        {
            if (!_schemaReady)
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
                await db.Database.EnsureDeletedAsync();
                await db.Database.EnsureCreatedAsync();

                // The adapter assumes the bucket exists (provisioning is an
                // operational concern); create it for the test run.
                var s3 = Services.GetRequiredService<IAmazonS3>();
                var buckets = await s3.ListBucketsAsync();
                if (buckets.Buckets is null || !buckets.Buckets.Any(b => b.BucketName == Bucket))
                {
                    await s3.PutBucketAsync(Bucket);
                }

                _schemaReady = true;
            }
        }
        finally
        {
            SchemaGate.Release();
        }
    }

    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    /// <summary>Seeds a user directly (bcrypt-hashed) and returns its id.</summary>
    public async Task<Guid> SeedUserAsync(
        string username, string password, string role, DateTimeOffset? onboardingCompletedAt)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = $"{username}@contract.test",
            PasswordHash = hasher.Hash(password),
            FirstName = "Contract",
            FirstLastName = "Test",
            OnboardingCompletedAt = onboardingCompletedAt,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        user.Roles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Role = role,
            Domain = RoleCatalog.DomainOf(role) ?? "System",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
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

    public async Task<T> WithDbAsync<T>(Func<HsmDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        return await work(db);
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> work)
    {
        using var scope = Services.CreateScope();
        await work(scope.ServiceProvider);
    }
}
