using Hsm.Infrastructure;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

/// <summary>
/// Builds the real composition (AddHsmInfrastructure) against real services.
/// Defaults target the dev container's service names; CI overrides via
/// environment variables (see .github/workflows/pr-validation.yml).
/// </summary>
public static class TestServices
{
    public static IConfiguration Configuration { get; } = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:HsmDb"] =
                "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=hsm_test",
            ["Redis:ConnectionString"] = "redis:6379,user=redis,password=redis",
            ["Storage:S3:Endpoint"] = "http://rustfs:9000",
            ["Storage:S3:AccessKey"] = "rustfs_user",
            ["Storage:S3:SecretKey"] = "rustfs_password",
            ["Storage:S3:ForcePathStyle"] = "true",
            ["Storage:S3:Bucket"] = "hsm-integration-tests",
            ["Search:Meilisearch:Url"] = "http://meilisearch:7700",
            ["Search:Meilisearch:ApiKey"] = "meili_dev_master_key_please_rotate",
            ["Search:Collections:proving-notes"] = "meilisearch",
        })
        .AddEnvironmentVariables()
        .Build();

    public static ServiceProvider Build(
        Action<IConfigurationBuilder>? customize = null,
        Action<IServiceCollection>? customizeServices = null)
    {
        var configuration = Configuration;
        if (customize is not null)
        {
            var builder = new ConfigurationBuilder().AddConfiguration(Configuration);
            customize(builder);
            configuration = builder.Build();
        }

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddHsmInfrastructure(configuration);
        customizeServices?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Brings a suite's database to the current schema the way a deployment
    /// does — by APPLYING MIGRATIONS, the same path
    /// <c>Hsm.Api -- --migrate</c> takes.
    ///
    /// It used to be <c>EnsureCreated</c>, which builds the schema straight
    /// from the model and never reads a migration: a migration that had
    /// drifted from the model would still have passed every test here, and the
    /// first real database created from it would have been wrong. Now the
    /// tests run on the artifact that ships, so drift fails the suite.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// The same, for suites that need a schema no earlier run can have left
    /// rows in: drop the database, then migrate it back up from nothing —
    /// which also proves the baseline applies to an empty database.
    /// </summary>
    public static async Task RecreateAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }
}
