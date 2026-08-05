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
                return Task.CompletedTask;
            });
        });
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
