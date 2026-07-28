using System.Text.Json.Nodes;
using Hsm.Application.Errors;

namespace Hsm.Web.Fhir;

/// <summary>
/// FHIR routes bypass the frozen envelope entirely (fhir.decorator.ts +
/// fhir-operation-outcome.filter.ts): success responses are the RAW resource
/// or Bundle as plain application/json (the frozen Express default), and
/// errors render a FHIR OperationOutcome with content type
/// application/fhir+json. No metadata/data/issue wrapper ever appears here.
/// </summary>
public static class FhirResponses
{
    public const string FhirJsonContentType = "application/fhir+json";

    public static IResult Resource(JsonObject resource, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(resource, statusCode: statusCode);

    /// <summary>The frozen searchset Bundle shape (fhir-bundle.util.ts).</summary>
    public static IResult SearchsetBundle(IReadOnlyList<JsonObject> resources)
    {
        var entries = new JsonArray();
        foreach (var resource in resources)
        {
            entries.Add(new JsonObject { ["resource"] = resource });
        }

        var bundle = new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["type"] = "searchset",
            ["total"] = resources.Count,
            ["entry"] = entries,
        };
        return Results.Json(bundle);
    }

    /// <summary>
    /// Renders the frozen OperationOutcome error body: one issue whose
    /// severity is fatal at 5xx and error otherwise, the status-derived FHIR
    /// issue code, and the exception's own message as diagnostics (never PHI).
    /// </summary>
    public static async Task WriteOperationOutcomeAsync(HttpContext ctx, int statusCode, string diagnostics)
    {
        var outcome = new JsonObject
        {
            ["resourceType"] = "OperationOutcome",
            ["issue"] = new JsonArray(new JsonObject
            {
                ["severity"] = statusCode >= StatusCodes.Status500InternalServerError ? "fatal" : "error",
                ["code"] = IssueCode(statusCode),
                ["diagnostics"] = diagnostics,
            }),
        };
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = FhirJsonContentType;
        await ctx.Response.WriteAsync(outcome.ToJsonString());
    }

    /// <summary>
    /// Wraps a FHIR endpoint so application failures render OperationOutcome
    /// instead of the /v1 error envelope (the frozen controller-scoped filter
    /// shadowing the global ResponseFilter).
    /// </summary>
    public static async Task ExecuteAsync(HttpContext ctx, Func<Task<IResult>> endpoint)
    {
        try
        {
            var result = await endpoint();
            await result.ExecuteAsync(ctx);
        }
        catch (ApiException exception) when (!ctx.Response.HasStarted)
        {
            await WriteOperationOutcomeAsync(ctx, exception.StatusCode, exception.IssueMessage ?? "Error");
        }
        catch (Exception) when (!ctx.Response.HasStarted)
        {
            await WriteOperationOutcomeAsync(
                ctx, StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    /// <summary>The frozen HTTP-status → FHIR IssueType map (fhir-operation-outcome.filter.ts).</summary>
    private static string IssueCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status404NotFound => "not-found",
        StatusCodes.Status422UnprocessableEntity or StatusCodes.Status400BadRequest => "invalid",
        StatusCodes.Status409Conflict => "duplicate",
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => "forbidden",
        >= StatusCodes.Status500InternalServerError => "exception",
        _ => "processing",
    };
}
