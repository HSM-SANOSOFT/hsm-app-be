using Amazon.Runtime;
using Amazon.S3;
using Hsm.Application.Auth;
using Hsm.Application.Coms;
using Hsm.Application.Ports;
using Hsm.Application.Proving;
using Hsm.Application.Settings;
using Hsm.Application.Templates;
using Hsm.Application.Users;
using Hsm.Infrastructure.Coms;
using Hsm.Infrastructure.Identity;
using Hsm.Infrastructure.Persistence;
using Hsm.Infrastructure.Search;
using Hsm.Infrastructure.Settings;
using Hsm.Infrastructure.Storage;
using Hsm.Infrastructure.Users;
using Meilisearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TemplateStore = Hsm.Infrastructure.Templates.TemplateStore;

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
        AddUsersAndSettings(services);
        AddTemplatesAndComs(services, configuration);

        return services;
    }

    /// <summary>
    /// Template + communications adapters and handlers (plan U14). Dispatch
    /// runs in-process (see <see cref="ChannelComsDispatcher"/> for the
    /// worker-topology decision); SMTP delivery stays a port with a logging
    /// adapter — real relays are deployment configuration.
    /// </summary>
    private static void AddTemplatesAndComs(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITemplateStore, TemplateStore>();
        services.AddSingleton<ITemplateRenderer, Hsm.Infrastructure.Templates.HandlebarsTemplateRenderer>();

        services.AddScoped<ListTemplatesHandler>();
        services.AddScoped<GetTemplateHandler>();
        services.AddScoped<CreateTemplateHandler>();
        services.AddScoped<UpdateTemplateHandler>();
        services.AddScoped<DeleteTemplateHandler>();
        services.AddScoped<ValidateTemplateHandler>();
        services.AddScoped<DraftRenderHandler>();
        services.AddScoped<TemplateParser>();

        services.AddScoped<IEmailBatchStore, EmailBatchStore>();
        services.AddScoped<IEmailSuppressionStore, EmailSuppressionStore>();
        services.AddScoped<IEmailWebhookEventStore, EmailWebhookEventStore>();
        services.AddSingleton<IEmailTransport, LoggingEmailTransport>();

        services.AddSingleton(new ComsQueueOptions
        {
            MaxAttempts = configuration.GetValue("Coms:MaxAttempts", defaultValue: 5),
            RetryBaseDelay = TimeSpan.FromMilliseconds(
                configuration.GetValue("Coms:RetryBaseDelayMs", defaultValue: 5000)),
        });
        services.AddSingleton<ChannelComsDispatcher>();
        services.AddSingleton<IComsJobDispatcher>(sp => sp.GetRequiredService<ChannelComsDispatcher>());
        services.AddHostedService<ComsJobProcessor>();

        services.AddScoped<SendEmailHandler>();
        services.AddScoped<ListEmailBatchesHandler>();
        services.AddScoped<GetEmailBatchHandler>();
        services.AddScoped<ResendEmailBatchHandler>();
        services.AddScoped<ListEmailRecipientsHandler>();
        services.AddScoped<GetEmailRecipientHandler>();
        services.AddScoped<ResendEmailRecipientHandler>();
        services.AddScoped<ReceiveWebhookHandler>();
        services.AddScoped<SendEmailJobHandler>();
        services.AddScoped<ProcessWebhookJobHandler>();
    }

    /// <summary>
    /// User-administration and settings adapters plus their use-case handlers
    /// (plan U13). The settings seed source binds the frozen envValue()
    /// fallbacks from configuration (Settings:Seed:&lt;KEY&gt;).
    /// </summary>
    private static void AddUsersAndSettings(IServiceCollection services)
    {
        services.AddSingleton<IStaffWelcomeEmailer, LoggingStaffWelcomeEmailer>();
        services.AddScoped<IAppSettingStore, AppSettingStore>();
        services.AddSingleton<ISettingSeedSource, ConfigurationSettingSeedSource>();

        services.AddScoped<UpdateOwnProfileHandler>();
        services.AddScoped<ChangeOwnPasswordHandler>();
        services.AddScoped<CreateStaffHandler>();
        services.AddScoped<ListUsersHandler>();
        services.AddScoped<GetUserHandler>();
        services.AddScoped<ChangeUserRoleHandler>();
        services.AddScoped<GetSettingsHandler>();
        services.AddScoped<UpdateSettingsHandler>();
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
