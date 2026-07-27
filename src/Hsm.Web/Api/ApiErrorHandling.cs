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

            try
            {
                await next();
            }
            catch (ApiException exception) when (!ctx.Response.HasStarted)
            {
                await ApiEnvelope.WriteErrorAsync(ctx, exception.StatusCode, ApiEnvelope.IssueFor(exception));
            }
            catch (Exception) when (!ctx.Response.HasStarted)
            {
                // The frozen API surfaced unhandled errors as a bare Nest 500;
                // here the envelope invariant is kept (issue.code COMMON.INTERNAL)
                // — a deliberate, documented divergence.
                await ApiEnvelope.WriteErrorAsync(
                    ctx,
                    StatusCodes.Status500InternalServerError,
                    new JsonObject { ["message"] = "Internal server error" });
            }
        });
    }
}
