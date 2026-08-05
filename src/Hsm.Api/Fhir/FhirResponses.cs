using System.Text.Json.Nodes;
using FluentValidation;
using Hsm.Application.Errors;

namespace Hsm.Api.Fhir;

/// <summary>
/// FHIR routes bypass the standard envelope entirely: success responses are
/// the RAW resource or Bundle as plain application/json, and errors render a
/// FHIR OperationOutcome with content type application/fhir+json. No
/// metadata/data/issue wrapper ever appears here.
/// </summary>
public static class FhirResponses
{
    public const string FhirJsonContentType = "application/fhir+json";

    public static IResult Resource(JsonObject resource, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(resource, statusCode: statusCode);

    /// <summary>The searchset Bundle shape.</summary>
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
    /// Renders the OperationOutcome error body: one issue whose
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
    /// instead of problem+json. The FHIR door is the ONE place a validation
    /// failure is not a 400: FHIR clients expect 422 for a resource they sent
    /// that the server could not accept, and the R4 spec's own examples use it.
    /// Everything else is the same closed set, same statuses.
    /// </summary>
    public static async Task ExecuteAsync(HttpContext ctx, Func<Task<IResult>> endpoint)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            var result = await endpoint();
            await result.ExecuteAsync(ctx);
        }
        catch (Exception exception) when (!ctx.Response.HasStarted)
        {
            var (status, diagnostics) = Classify(exception);
            await WriteOperationOutcomeAsync(ctx, status, diagnostics);
        }
    }

    private static (int Status, string Diagnostics) Classify(Exception exception) => exception switch
    {
        ValidationException validation => (
            StatusCodes.Status422UnprocessableEntity,
            string.Join("; ", validation.Errors.Select(e => e.ErrorMessage))),
        UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
        NotFoundException notFound => (StatusCodes.Status404NotFound, notFound.Message),
        ConflictException conflict => (StatusCodes.Status409Conflict, conflict.Message),
        TooManyRequestsException => (StatusCodes.Status429TooManyRequests, "Too Many Requests"),
        _ => (StatusCodes.Status500InternalServerError, "Internal server error"),
    };

    /// <summary>The HTTP-status → FHIR IssueType map, formerly ErrorStatusCodes.</summary>
    private static string IssueCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not-found",
        StatusCodes.Status409Conflict => "duplicate",
        StatusCodes.Status429TooManyRequests => "processing",
        StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity => "invalid",
        >= StatusCodes.Status500InternalServerError => "exception",
        _ => "processing",
    };
}
