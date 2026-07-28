using System.Reflection;
using System.Text.Json.Nodes;
using Hsm.Web.Api;

namespace Hsm.Web.Health;

/// <summary>
/// The two frozen public health operations (main.controller.ts).
///
/// GET /v1/health reproduces the frozen Terminus report EXACTLY: the frozen
/// controller ran health.check([]) — an EMPTY indicator list — so the report
/// is always status "ok" with empty info/error/details, wrapped in the
/// success envelope. No dependency probes ran in the frozen system and none
/// run here; adding them would change both the report body and the
/// status-code behavior (Terminus 503s on a failed indicator).
///
/// GET /v1/health/version returns ONLY the semantic version (frozen
/// main.service.ts: API_VERSION env, then the package version, then "0.0.0")
/// — no git SHA/branch/build timestamp, so anonymous callers get no
/// exact-build reconnaissance.
/// </summary>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var health = app.MapGroup("/v1/health");
        health.MapGet("", (Delegate)Check);
        health.MapGet("/version", (Delegate)Version);
    }

    private static IResult Check(HttpContext ctx) =>
        ApiEnvelope.Success(ctx, StatusCodes.Status200OK, new JsonObject
        {
            ["status"] = "ok",
            ["info"] = new JsonObject(),
            ["error"] = new JsonObject(),
            ["details"] = new JsonObject(),
        });

    /// <summary>The npm_package_version analog: the host assembly's semantic
    /// version, resolved once. Build metadata (+sha) is stripped — the frozen
    /// endpoint never exposed it.</summary>
    private static readonly string? AssemblyVersion = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        .Split('+')[0];

    private static IResult Version(HttpContext ctx, IConfiguration configuration)
    {
        var configured = configuration["API_VERSION"];
        var version = string.IsNullOrEmpty(configured)
            ? AssemblyVersion ?? "0.0.0"
            : configured;
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, new JsonObject
        {
            ["version"] = version,
        });
    }
}
