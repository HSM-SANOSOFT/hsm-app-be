using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Hsm.Api.OpenApi;

/// <summary>
/// The generated document and the reference UI, both gated by
/// <c>OpenApi:Enabled</c> — on in Development, off by default everywhere else.
/// A public schema of every route is a reconnaissance gift, and turning it on
/// in production should be a decision somebody makes in configuration rather
/// than a default nobody noticed.
/// </summary>
public static class OpenApiRegistration
{
    public const string DocumentName = "v1";

    /// <summary>The two ways a caller authenticates, as they appear in the spec.</summary>
    private const string CookieScheme = "session";
    private const string BearerScheme = "bearer";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        configuration.GetValue("OpenApi:Enabled", defaultValue: environment.IsDevelopment());

    public static IServiceCollection AddHsmOpenApi(
        this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        if (!IsEnabled(environment, configuration))
        {
            return services;
        }

        return services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "HSM API";
                document.Info.Version = "v1";
                // The servers list is whatever host generated the document,
                // which would make the committed artifact differ between a
                // developer's machine, CI, and the test harness. The spec
                // describes the SURFACE; the base URL is the deployment's.
                document.Servers?.Clear();
                DescribeSecuritySchemes(document);
                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                RequireAmbientCredential(operation, context);
                return Task.CompletedTask;
            });
        });
    }

    /// <summary>
    /// Marks a route whose 401 answers "the credential you sent in the BODY is
    /// wrong", not "you must authenticate first" — sign-in, integration token
    /// refresh, the provider webhook's signature check. They are reached
    /// anonymously and must not carry a security requirement; without this
    /// marker <see cref="RequireAmbientCredential"/> would read their 401 as an
    /// authentication gate and document sign-in as needing a session.
    /// </summary>
    public static TBuilder ExchangesCredentials<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(CredentialExchange.Marker);
    }

    private sealed class CredentialExchange
    {
        public static readonly CredentialExchange Marker = new();
    }

    /// <summary>The two authentication mechanisms, published once in components.</summary>
    private static void DescribeSecuritySchemes(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        document.Components.SecuritySchemes[CookieScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = "hsm.session",
            Description =
                "The browser session. Issued by POST /api/v1/identity/login, encrypted, HttpOnly and "
                + "SameSite=Strict, and revocable per session by POST /api/v1/identity/logout. Unsafe "
                + "methods authenticated this way ALSO require the antiforgery token from "
                + "GET /api/v1/identity/csrf, echoed in the X-XSRF-TOKEN header.",
        };

        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description =
                "An integration's access token, from POST /api/v1/identity/integrations/{id}/tokens or "
                + "GET /v1/auth/refresh. Short-lived and rotated; carrying it opts the request out of "
                + "the cookie handler entirely, so no antiforgery token is required or accepted.",
        };
    }

    /// <summary>
    /// Says which operations need one of those mechanisms.
    ///
    /// <para>It is not discoverable from the framework's own metadata:
    /// authorization in this system rides on the REQUEST type and is enforced
    /// once by <c>AuthorizationBehavior</c>, so no endpoint carries
    /// <c>RequireAuthorization</c> for the generator to notice, and left alone
    /// the spec would describe a surface anyone can call. The signal used
    /// instead is the one the routes DO declare: a route that documents a 401 is
    /// a route that authenticates — minus the handful that answer 401 about a
    /// credential in the request body rather than an ambient one, which say so
    /// with <see cref="ExchangesCredentials{TBuilder}"/>.</para>
    ///
    /// <para>Every authenticating route accepts EITHER mechanism (the adaptive
    /// scheme picks by <c>Authorization</c> header — see
    /// <c>AddHsmIdentityAuthentication</c>), so this emits TWO single-entry
    /// requirement objects rather than one with two entries: in OpenAPI that is
    /// OR, which is what the selector actually does.</para>
    /// </summary>
    private static void RequireAmbientCredential(
        OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        if (operation.Responses?.ContainsKey("401") != true
            || context.Description.ActionDescriptor.EndpointMetadata.OfType<CredentialExchange>().Any())
        {
            return;
        }

        operation.Security =
        [
            new() { [new OpenApiSecuritySchemeReference(CookieScheme, context.Document)] = [] },
            new() { [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [] },
        ];
    }

    public static void MapHsmOpenApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!IsEnabled(app.Environment, app.Configuration))
        {
            return;
        }

        app.MapOpenApi("/api/openapi.json");
        app.MapScalarApiReference("/api", options => options
            .WithTitle("HSM API")
            .WithOpenApiRoutePattern("/api/openapi.json"));
    }
}
