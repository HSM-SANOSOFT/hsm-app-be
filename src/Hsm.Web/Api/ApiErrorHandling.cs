using System.Text.Json.Nodes;
using Hsm.Application.Errors;

namespace Hsm.Web.Api;

/// <summary>
/// Renders application failures on API routes (/v1/*) as the frozen error
/// envelope. Non-API routes (the Blazor surface) are untouched.
/// </summary>
public static class ApiErrorHandling
{
    public static void UseApiErrorEnvelope(this IApplicationBuilder app)
    {
        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/v1"))
            {
                await next();
                return;
            }

            await ApiErrorGuard.RunAsync(
                ctx,
                () => next(),
                (c, exception) =>
                    ApiEnvelope.WriteErrorAsync(c, exception.StatusCode, ApiEnvelope.IssueFor(exception)),
                // The frozen API surfaced unhandled errors as a bare Nest 500;
                // here the envelope invariant is kept (issue.code COMMON.INTERNAL)
                // — a deliberate, documented divergence.
                c => ApiEnvelope.WriteErrorAsync(
                    c,
                    StatusCodes.Status500InternalServerError,
                    new JsonObject { ["message"] = "Internal server error" }));
        });
    }
}
