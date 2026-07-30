using Hsm.Infrastructure;
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
}
