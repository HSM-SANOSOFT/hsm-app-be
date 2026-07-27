using Amazon.Runtime;
using Amazon.S3;
using Hsm.Application.Auth;
using Hsm.Application.Ports;
using Hsm.Application.Proving;
using Hsm.Infrastructure.Identity;
using Hsm.Infrastructure.Persistence;
using Hsm.Infrastructure.Search;
using Hsm.Infrastructure.Storage;
using Meilisearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Infrastructure;

/// <summary>
/// Composition-root entry point: binds every adapter from configuration.
/// Topology lives in config — same binary, different endpoints.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddHsmInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // PostgreSQL via EF Core. Concretely PostgreSQL on purpose: jsonb,
        // arrays, partial indexes, and generated columns are wanted; there is
        // no second engine to stay portable for.
        services.AddDbContext<HsmDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("HsmDb")));
        services.AddScoped<IProvingRepository, ProvingRepository>();

        // Distributed cache through the framework's standard abstraction.
        services.AddStackExchangeRedisCache(options =>
            options.Configuration = configuration["Redis:ConnectionString"]);

        // S3-compatible blob storage against a configured endpoint.
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(
                configuration["Storage:S3:AccessKey"],
                configuration["Storage:S3:SecretKey"]),
            new AmazonS3Config
            {
                ServiceURL = configuration["Storage:S3:Endpoint"],
                ForcePathStyle = configuration.GetValue("Storage:S3:ForcePathStyle", defaultValue: true),
                AuthenticationRegion = configuration["Storage:S3:Region"] ?? "us-east-1",
            }));
        services.AddSingleton<IObjectStorage>(sp => new S3ObjectStorage(
            sp.GetRequiredService<IAmazonS3>(),
            configuration["Storage:S3:Bucket"] ?? "hsm"));

        // Search: one Meilisearch client, engine-per-collection resolution.
        services.AddSingleton(_ => new MeilisearchClient(
            configuration["Search:Meilisearch:Url"],
            configuration["Search:Meilisearch:ApiKey"]));
        services.AddSingleton<ISearchIndexResolver, SearchIndexResolver>();

        AddIdentity(services, configuration);

        return services;
    }

    /// <summary>
    /// Identity adapters and auth use-case handlers (plan U12). Two refresh
    /// token stores on purpose: browser sessions and integration tokens never
    /// share persistence.
    /// </summary>
    private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IUserStore, UserStore>();
        services.AddScoped<IUserRefreshTokenStore, UserRefreshTokenStore>();
        services.AddScoped<IIntegrationRefreshTokenStore, IntegrationRefreshTokenStore>();
        services.AddScoped<IIntegrationAccountStore, IntegrationAccountStore>();
        services.AddScoped<IPasswordResetTokenStore, PasswordResetTokenStore>();
        services.AddScoped<IAuthUnitOfWork, AuthUnitOfWork>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IRecoveryEmailer, LoggingRecoveryEmailer>();

        services.AddSingleton(new AuthTokenOptions
        {
            AccessSecret = configuration["Auth:JwtAccessSecret"] ?? string.Empty,
            RefreshSecret = configuration["Auth:JwtRefreshSecret"] ?? string.Empty,
        });
        services.AddSingleton<IAuthTokenCodec, JwtAuthTokenCodec>();

        // Frozen envs.ENVIRONMENT gate for the developer role.
        var environment = configuration["Auth:Environment"] ?? "dev";
        services.AddSingleton<IEnvironmentPolicy>(new EnvironmentPolicy(environment == "dev"));

        services.AddScoped<TokenIssuer>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<SignupHandler>();
        services.AddScoped<SignupIntegrationHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<LogoutIntegrationHandler>();
        services.AddScoped<CompleteOnboardingHandler>();
        services.AddScoped<ForgotPasswordHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<RecoverUsernameHandler>();
    }

    private sealed class EnvironmentPolicy(bool isDev) : IEnvironmentPolicy
    {
        public bool IsDev { get; } = isDev;
    }
}
