using Hsm.Application.Abstractions;
using Hsm.Application.System.Queries.GetSystemStatus;

// Namespace is Hsm.Api.SystemStatus, not Hsm.Api.System, though the file
// lives in src/Hsm.Api/System per the task's file list: a namespace segment
// named exactly "System" directly under Hsm.Api shadows the BCL System
// namespace for every sibling file nested under Hsm.Api (Fhir, Emails, ...)
// that references System.* by qualified name rather than a using directive —
// confirmed by a build break in FhirEndpoints.cs and EmailResource.cs when
// this was tried. Hsm.Application.System (GetSystemStatusQuery's namespace)
// does not hit this because no sibling file under Hsm.Application happens to
// qualify a System.* name inline.
namespace Hsm.Api.SystemStatus;

/// <summary>
/// <c>GET /health</c> is standard ASP.NET Core health-check middleware
/// (<c>Program.cs</c>: <c>AddHealthChecks()</c> with no registered checks,
/// <c>MapHealthChecks("/health")</c>) — liveness only, so there is no
/// delegate for it here.
///
/// <c>GET /api/v1/system/status</c> dispatches the existing
/// <see cref="GetSystemStatusQuery"/> (Application layer, unchanged — it
/// exists to prove the U8 Blazor UI-service boundary round trip and is
/// reused as-is here) and layers on the fields the retired
/// <c>GET /v1/health/version</c> route and this dashboard-facing route need
/// that <see cref="Hsm.Contracts.Ui.SystemStatusDto"/> does not carry:
///
/// <list type="bullet">
/// <item><description><c>Version</c>: resolved with the same precedence the
/// retired <c>HealthEndpoints.Version</c> implemented — the <c>API_VERSION</c>
/// configuration value, then the dispatched result's version with any
/// <c>+build</c> suffix stripped, then <c>"0.0.0"</c>.</description></item>
/// <item><description><c>Environment</c>: <see cref="IHostEnvironment.EnvironmentName"/>
/// — standard host information with no domain analog to dispatch a query
/// for.</description></item>
/// <item><description><c>CheckedAt</c>: the instant this response is
/// assembled.</description></item>
/// </list>
///
/// No <c>Components</c>/dependency-state field — see
/// <see cref="SystemStatusResource"/>'s doc comment for why.
/// </summary>
public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/api/v1/system/status", GetStatus)
            .WithTags("System")
            .WithSummary("Application version, environment, and dependency status for a dashboard.")
            .Produces<SystemStatusResource>();
    }

    private static async Task<IResult> GetStatus(
        IDispatcher dispatcher, IHostEnvironment environment, IConfiguration configuration, CancellationToken ct)
    {
        var status = await dispatcher.Send(new GetSystemStatusQuery(), ct);

        var configured = configuration["API_VERSION"];
        var version = !string.IsNullOrEmpty(configured)
            ? configured
            : string.IsNullOrEmpty(status.Version) || status.Version == "unknown"
                ? "0.0.0"
                : status.Version.Split('+')[0];

        return Results.Ok(new SystemStatusResource(
            version,
            environment.EnvironmentName,
            DateTimeOffset.UtcNow));
    }
}
