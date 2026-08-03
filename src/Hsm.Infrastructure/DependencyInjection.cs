using Amazon.Runtime;
using Amazon.S3;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Auth.Commands.CompleteOnboarding;
using Hsm.Application.Auth.Commands.ForgotPassword;
using Hsm.Application.Auth.Commands.GeneratePin;
using Hsm.Application.Auth.Commands.IssueIntegrationTokens;
using Hsm.Application.Auth.Commands.Login;
using Hsm.Application.Auth.Commands.Logout;
using Hsm.Application.Auth.Commands.LogoutIntegration;
using Hsm.Application.Auth.Commands.RecoverUsername;
using Hsm.Application.Auth.Commands.RefreshTokens;
using Hsm.Application.Auth.Commands.ResetPassword;
using Hsm.Application.Auth.Commands.RevokeIntegrationTokens;
using Hsm.Application.Auth.Commands.Signup;
using Hsm.Application.Auth.Commands.SignupIntegration;
using Hsm.Application.Auth.Commands.ValidatePin;
using Hsm.Application.Auth.Queries.ListIntegrationAccounts;
using Hsm.Application.Clinical;
using Hsm.Application.Clinical.Commands.CreatePatient;
using Hsm.Application.Clinical.Queries.GetPatient;
using Hsm.Application.Clinical.Queries.SearchPatients;
using Hsm.Application.Coms;
using Hsm.Application.Coms.Commands.DispatchEmailBatch;
using Hsm.Application.Coms.Commands.ProcessWebhookEvent;
using Hsm.Application.Coms.Commands.ReceiveWebhook;
using Hsm.Application.Coms.Commands.ResendEmailBatch;
using Hsm.Application.Coms.Commands.ResendEmailRecipient;
using Hsm.Application.Coms.Commands.SendEmail;
using Hsm.Application.Coms.Queries.GetEmailBatch;
using Hsm.Application.Coms.Queries.GetEmailRecipient;
using Hsm.Application.Coms.Queries.ListEmailBatches;
using Hsm.Application.Coms.Queries.ListEmailRecipients;
using Hsm.Application.Docs;
using Hsm.Application.Docs.Commands.DeleteDocument;
using Hsm.Application.Docs.Commands.GenerateDocument;
using Hsm.Application.Docs.Commands.RenderDocument;
using Hsm.Application.Docs.Commands.UploadDocuments;
using Hsm.Application.Docs.Queries.GetDocument;
using Hsm.Application.Docs.Queries.GetDocumentUrl;
using Hsm.Application.Docs.Queries.ListDocuments;
using Hsm.Application.Docs.Queries.PresignDocuments;
using Hsm.Application.Ports;
using Hsm.Application.Proving;
using Hsm.Application.Settings;
using Hsm.Application.Settings.Commands.UpdateSettings;
using Hsm.Application.Settings.Queries.GetSettings;
using Hsm.Application.Settings.Queries.ListSettingsAudit;
using Hsm.Application.Templates;
using Hsm.Application.Templates.Commands.CreateTemplate;
using Hsm.Application.Templates.Commands.DeleteTemplate;
using Hsm.Application.Templates.Commands.UpdateTemplate;
using Hsm.Application.Templates.Queries.DraftRender;
using Hsm.Application.Templates.Queries.GetTemplate;
using Hsm.Application.Templates.Queries.ListTemplates;
using Hsm.Application.Templates.Queries.ValidateTemplate;
using Hsm.Application.Users;
using Hsm.Application.Users.Commands.ChangeOwnPassword;
using Hsm.Application.Users.Commands.ChangeUserRole;
using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Application.Users.Commands.UpdateOwnProfile;
using Hsm.Application.Users.Queries.GetUser;
using Hsm.Application.Users.Queries.ListUsers;
using Hsm.Domain.Clinical;
using Hsm.Domain.Coms;
using Hsm.Domain.Docs;
using Hsm.Domain.Identity;
using Hsm.Domain.Settings;
using Hsm.Domain.Templates;
using Hsm.Infrastructure.Clinical;
using Hsm.Infrastructure.Coms;
using Hsm.Infrastructure.Docs;
using Hsm.Infrastructure.Identity;
using Hsm.Infrastructure.Jobs;
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
        // Several adapters here take ILogger<T>. The hosts get logging from
        // their builders, but a bare ServiceCollection (the integration tests)
        // does not, and the failure is a resolution error far from its cause.
        // AddLogging uses TryAdd, so it never overrides a host's configuration.
        services.AddLogging();

        // PostgreSQL via EF Core. Concretely PostgreSQL on purpose: jsonb,
        // arrays, partial indexes, and generated columns are wanted; there is
        // no second engine to stay portable for.
        services.AddDbContext<HsmDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("HsmDb")));
        services.AddScoped<IProvingRepository, ProvingRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

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
        // Presigned URLs may sign against a second, externally reachable
        // endpoint (frozen STRG_S3_HOST_EXTERNAL) so browsers outside the
        // container network can use them; unset means the main client signs.
        services.AddSingleton<IObjectStorage>(sp =>
        {
            var externalEndpoint = configuration["Storage:S3:ExternalEndpoint"];
            var presignClient = string.IsNullOrEmpty(externalEndpoint)
                ? null
                : new AmazonS3Client(
                    new BasicAWSCredentials(
                        configuration["Storage:S3:AccessKey"],
                        configuration["Storage:S3:SecretKey"]),
                    new AmazonS3Config
                    {
                        ServiceURL = externalEndpoint,
                        ForcePathStyle = configuration.GetValue("Storage:S3:ForcePathStyle", defaultValue: true),
                        AuthenticationRegion = configuration["Storage:S3:Region"] ?? "us-east-1",
                    });
            return new S3ObjectStorage(
                sp.GetRequiredService<IAmazonS3>(),
                configuration["Storage:S3:Bucket"] ?? "hsm",
                presignClient);
        });

        // Search: one Meilisearch client, engine-per-collection resolution.
        services.AddSingleton(_ => new MeilisearchClient(
            configuration["Search:Meilisearch:Url"],
            configuration["Search:Meilisearch:ApiKey"]));
        services.AddSingleton<ISearchIndexResolver, SearchIndexResolver>();

        // The durable job queue (Redis Streams). Producing side only: a host
        // that consumes registers the loop itself, and nothing here opens a
        // Redis connection — RedisJobConnection connects on first use.
        services.AddHsmJobQueue(configuration);

        AddIdentity(services, configuration);
        AddClinical(services);
        AddUsersAndSettings(services);
        AddTemplatesAndComs(services, configuration);
        AddDocs(services, configuration);

        return services;
    }

    /// <summary>
    /// Documents adapters and handlers (plan U15). Generation runs as a queued
    /// RenderDocumentCommand on the 'docs' queue — frozen retry posture (3
    /// attempts, 1s first-attempt delay, 2s exponential backoff) configured in
    /// JobQueueRegistration — with QuestPDF instead of headless Chrome.
    /// </summary>
    private static void AddDocs(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDocumentStore, DocumentStore>();
        services.AddSingleton<IDocumentPdfRenderer, QuestPdfDocumentRenderer>();
        services.AddSingleton(new DocsOptions
        {
            Bucket = configuration["Docs:Bucket"] ?? "hsm-docs",
        });

        // Docs: command/query slices behind the dispatcher. Policy rides on
        // the request type, exactly as Templates/Users/Settings/Auth/Coms —
        // including RenderDocumentCommand, which the queue consumer dispatches
        // through the same pipeline as everything else.
        services.AddScoped<
            IRequestHandler<ListDocumentsQuery, ListDocumentsResult>, ListDocumentsHandler>();
        services.AddScoped<
            IRequestHandler<GenerateDocumentCommand, GenerateDocumentResult>, GenerateDocumentHandler>();
        services.AddScoped<IRequestHandler<GetDocumentQuery, Document>, GetDocumentHandler>();
        services.AddScoped<IRequestHandler<GetDocumentUrlQuery, string>, GetDocumentUrlHandler>();
        services.AddScoped<IRequestHandler<DeleteDocumentCommand, Unit>, DeleteDocumentHandler>();
        services.AddScoped<
            IRequestHandler<PresignDocumentsQuery, IReadOnlyList<PresignedItem>>, PresignDocumentsHandler>();
        services.AddScoped<
            IRequestHandler<UploadDocumentsCommand, UploadDocumentsResult>, UploadDocumentsHandler>();
        services.AddScoped<IRequestHandler<RenderDocumentCommand, Unit>, RenderDocumentHandler>();
    }

    /// <summary>
    /// Template + communications adapters and handlers (plan U14). Sending runs
    /// as a queued DispatchEmailBatchCommand on the strictly serial 'coms'
    /// queue (frozen resend ordering); SMTP delivery stays a port with a
    /// logging adapter — real relays are deployment configuration.
    /// </summary>
    private static void AddTemplatesAndComs(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITemplateStore, TemplateStore>();
        services.AddSingleton<ITemplateRenderer, Hsm.Infrastructure.Templates.HandlebarsTemplateRenderer>();

        // Templates: command/query slices behind the dispatcher. Policy rides
        // on the request type; ValidateTemplate/DraftRender are IQuery (never
        // open a transaction — they compute, they do not persist).
        services.AddScoped<IRequestHandler<ListTemplatesQuery, IReadOnlyList<Template>>, ListTemplatesHandler>();
        services.AddScoped<IRequestHandler<GetTemplateQuery, Template>, GetTemplateHandler>();
        services.AddScoped<IRequestHandler<CreateTemplateCommand, Template>, CreateTemplateHandler>();
        services.AddScoped<IRequestHandler<UpdateTemplateCommand, Template>, UpdateTemplateHandler>();
        services.AddScoped<IRequestHandler<DeleteTemplateCommand, Unit>, DeleteTemplateHandler>();
        services.AddScoped<
            IRequestHandler<ValidateTemplateQuery, ValidateTemplateResult>, ValidateTemplateHandler>();
        services.AddScoped<IRequestHandler<DraftRenderQuery, string>, DraftRenderHandler>();
        services.AddScoped<TemplateParser>();

        services.AddScoped<IEmailBatchStore, EmailBatchStore>();
        services.AddScoped<IEmailSuppressionStore, EmailSuppressionStore>();
        services.AddScoped<IEmailWebhookEventStore, EmailWebhookEventStore>();
        services.AddSingleton<IEmailTransport, LoggingEmailTransport>();

        // Coms: command/query slices behind the dispatcher. Policy rides on
        // the request type, exactly as Templates/Users/Settings/Auth —
        // including the two job commands, which the queue consumer dispatches
        // through the same pipeline as everything else.
        services.AddScoped<IRequestHandler<SendEmailCommand, SendEmailResult>, SendEmailHandler>();
        services.AddScoped<
            IRequestHandler<ListEmailBatchesQuery, IReadOnlyList<EmailBatch>>, ListEmailBatchesHandler>();
        services.AddScoped<IRequestHandler<GetEmailBatchQuery, EmailBatch>, GetEmailBatchHandler>();
        services.AddScoped<IRequestHandler<ResendEmailBatchCommand, string>, ResendEmailBatchHandler>();
        services.AddScoped<
            IRequestHandler<ListEmailRecipientsQuery, IReadOnlyList<EmailRecipient>>, ListEmailRecipientsHandler>();
        services.AddScoped<IRequestHandler<GetEmailRecipientQuery, EmailRecipient>, GetEmailRecipientHandler>();
        services.AddScoped<
            IRequestHandler<ResendEmailRecipientCommand, string>, ResendEmailRecipientHandler>();
        services.AddScoped<
            IRequestHandler<ReceiveWebhookCommand, ReceiveWebhookResult>, ReceiveWebhookHandler>();
        services.AddScoped<IRequestHandler<DispatchEmailBatchCommand, Unit>, DispatchEmailBatchHandler>();
        services.AddScoped<IRequestHandler<ProcessWebhookEventCommand, Unit>, ProcessWebhookEventCommandHandler>();
    }

    /// <summary>
    /// Clinical patient adapter and handlers (plan U16): the pg-native
    /// patient-lookup surface behind /fhir/R4/Patient.
    /// </summary>
    private static void AddClinical(IServiceCollection services)
    {
        services.AddScoped<IPatientStore, PatientStore>();

        services.AddScoped<IRequestHandler<CreatePatientCommand, Patient>, CreatePatientHandler>();
        services.AddScoped<IRequestHandler<GetPatientQuery, Patient>, GetPatientHandler>();
        services.AddScoped<
            IRequestHandler<SearchPatientsQuery, IReadOnlyList<Patient>>, SearchPatientsHandler>();
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

        // Users: command/query slices behind the dispatcher (reference slice).
        // Policy rides on the request type, so there is nothing to register for
        // authorization — only the handler and its validators.
        services.AddScoped<IRequestHandler<UpdateOwnProfileCommand, User>, UpdateOwnProfileHandler>();
        services.AddScoped<IRequestHandler<ChangeOwnPasswordCommand, Unit>, ChangeOwnPasswordHandler>();
        services.AddScoped<IRequestHandler<CreateStaffUserCommand, User>, CreateStaffUserHandler>();
        services.AddScoped<IRequestHandler<ChangeUserRoleCommand, User>, ChangeUserRoleHandler>();
        services.AddScoped<IRequestHandler<ListUsersQuery, ListUsersResult>, ListUsersHandler>();
        services.AddScoped<IRequestHandler<GetUserQuery, User>, GetUserHandler>();

        services.AddScoped<IRequestHandler<GetSettingsQuery, SettingsView>, GetSettingsHandler>();
        services.AddScoped<IRequestHandler<UpdateSettingsCommand, SettingsView>, UpdateSettingsHandler>();
        services.AddScoped<
            IRequestHandler<ListSettingsAuditQuery, IReadOnlyList<AppSettingAudit>>, ListSettingsAuditHandler>();
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

        // Auth: command/query slices behind the dispatcher. Policy rides on the
        // request type (see each command's attributes), so nothing here grants
        // access — this is only handler wiring.
        services.AddScoped<IRequestHandler<LoginCommand, TokenPair>, LoginHandler>();
        services.AddScoped<IRequestHandler<SignupCommand, TokenPair>, SignupHandler>();
        services.AddScoped<IRequestHandler<RefreshTokensCommand, TokenPair>, RefreshTokensHandler>();
        services.AddScoped<IRequestHandler<LogoutCommand, Unit>, LogoutHandler>();
        services.AddScoped<IRequestHandler<SignupIntegrationCommand, TokenPair>, SignupIntegrationHandler>();
        services.AddScoped<IRequestHandler<LogoutIntegrationCommand, Unit>, LogoutIntegrationHandler>();
        services.AddScoped<IRequestHandler<CompleteOnboardingCommand, TokenPair>, CompleteOnboardingHandler>();
        services.AddScoped<IRequestHandler<ForgotPasswordCommand, Unit>, ForgotPasswordHandler>();
        services.AddScoped<IRequestHandler<ResetPasswordCommand, Unit>, ResetPasswordHandler>();
        services.AddScoped<IRequestHandler<RecoverUsernameCommand, Unit>, RecoverUsernameHandler>();
        services.AddScoped<IRequestHandler<GeneratePinCommand, Unit>, GeneratePinHandler>();
        services.AddScoped<IRequestHandler<ValidatePinCommand, Unit>, ValidatePinHandler>();

        // In-process UI surface only (plan U18): no /v1 routes map to these.
        services.AddScoped<
            IRequestHandler<ListIntegrationAccountsQuery, IReadOnlyList<IntegrationAccountListItem>>,
            ListIntegrationAccountsHandler>();
        services.AddScoped<IRequestHandler<IssueIntegrationTokensCommand, TokenPair>, IssueIntegrationTokensHandler>();
        services.AddScoped<IRequestHandler<RevokeIntegrationTokensCommand, int>, RevokeIntegrationTokensHandler>();
    }

    private sealed class EnvironmentPolicy(bool isDev) : IEnvironmentPolicy
    {
        public bool IsDev { get; } = isDev;
    }
}
