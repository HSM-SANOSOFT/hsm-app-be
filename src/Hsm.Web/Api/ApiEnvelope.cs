using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Hsm.Application;
using Hsm.Application.Errors;

namespace Hsm.Web.Api;

/// <summary>
/// The frozen response envelope (response.interceptor.ts / response.filter.ts):
/// success wraps payloads as { metadata, data }, errors as { metadata, issue }
/// with a stable machine-readable issue.code. Integration consumers depend on
/// this shape — it is contract, not decoration.
/// </summary>
public static partial class ApiEnvelope
{
    public static IResult Success(
        HttpContext ctx, int statusCode, JsonNode? data = null, bool includeData = true, JsonObject? extra = null)
    {
        var metadata = Metadata(ctx, statusCode, success: statusCode is >= 200 and < 300);
        if (extra is not null)
        {
            // metadata.extra — the frozen carrier for pagination and friends.
            metadata["extra"] = extra;
        }

        var body = new JsonObject
        {
            ["metadata"] = metadata,
        };
        if (includeData)
        {
            body["data"] = data;
        }

        return Results.Json(body, statusCode: statusCode);
    }

    public static async Task WriteErrorAsync(HttpContext ctx, int statusCode, JsonObject issue)
    {
        // Guarantee a stable code on every error envelope.
        issue["code"] ??= CodeForStatus(statusCode);

        var body = new JsonObject
        {
            ["metadata"] = Metadata(ctx, statusCode, success: false),
            ["issue"] = issue,
        };
        ctx.Response.StatusCode = statusCode;
        await ctx.Response.WriteAsJsonAsync(body);
    }

    public static JsonObject IssueFor(ApiException exception)
    {
        if (exception is ApiValidationException webValidation)
        {
            return ValidationIssue(webValidation.Messages, webValidation.FieldConstraints);
        }

        if (exception.ValidationFailures.Count > 0)
        {
            var messages = exception.ValidationFailures.Select(f => f.Message).ToList();
            var byField = exception.ValidationFailures
                .GroupBy(f => f.Field)
                .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(f => f.Key).Distinct().ToList()))
                .ToList();
            return ValidationIssue(messages, byField);
        }

        var issue = new JsonObject();
        if (exception.IssueMessage is not null)
        {
            issue["message"] = exception.IssueMessage;
        }

        if (exception.ErrorLabel is not null)
        {
            issue["error"] = exception.ErrorLabel;
        }

        if (exception.Code is not null)
        {
            issue["code"] = exception.Code;
        }

        return issue;
    }

    /// <summary>
    /// The frozen ValidationPipe issue shape shared by Hsm.Web's edge-level
    /// ApiValidationException and Hsm.Application's ApiException.Validation:
    /// issue.message as an ordered string array, issue.errors as per-field
    /// distinct constraint keys.
    /// </summary>
    private static JsonObject ValidationIssue(
        IReadOnlyList<string> messages, IReadOnlyList<(string Field, IReadOnlyList<string> Constraints)> fieldConstraints)
    {
        var issue = new JsonObject { ["code"] = ApiErrorCode.Validation };
        var messageArray = new JsonArray();
        foreach (var message in messages)
        {
            messageArray.Add(message);
        }

        issue["message"] = messageArray;
        var errors = new JsonArray();
        foreach (var (field, constraints) in fieldConstraints)
        {
            var keys = new JsonArray();
            foreach (var key in constraints)
            {
                keys.Add(key);
            }

            errors.Add(new JsonObject { ["field"] = field, ["constraints"] = keys });
        }

        issue["errors"] = errors;
        return issue;
    }

    /// <summary>The frozen status→code fallback map (response.filter.ts).</summary>
    private static string CodeForStatus(int status) => ErrorStatusCodes.For(status).EnvelopeCode;

    /// <summary>
    /// The frozen buildPaginationMeta shape: metadata.extra.pagination with
    /// query-driven page/pageSize and computed totals.
    /// </summary>
    public static JsonObject Pagination(int page, int pageSize, int totalItems) => new()
    {
        ["pagination"] = new JsonObject
        {
            ["page"] = page,
            ["pageSize"] = pageSize,
            ["totalItems"] = totalItems,
            ["totalPages"] = pageSize > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 0,
        },
    };

    /// <summary>
    /// The frozen response interceptor's synthesized pagination for bare-array
    /// payloads: page 1, pageSize = totalItems = the returned length,
    /// totalPages always 1.
    /// </summary>
    public static JsonObject SinglePagePagination(int count) => new()
    {
        ["pagination"] = new JsonObject
        {
            ["page"] = 1,
            ["pageSize"] = count,
            ["totalItems"] = count,
            ["totalPages"] = 1,
        },
    };

    private static JsonObject Metadata(HttpContext ctx, int statusCode, bool success)
    {
        var path = ctx.Request.Path + ctx.Request.QueryString;
        var metadata = new JsonObject
        {
            ["success"] = success,
            ["statusCode"] = statusCode,
            ["timestamp"] = IsoTimestamp.Of(DateTimeOffset.UtcNow),
            ["path"] = path,
            ["message"] = success ? "Request processed successfully." : "Request processed unsuccessfully.",
        };
        var version = VersionPattern().Match(path);
        if (version.Success)
        {
            metadata["apiVersion"] = $"v{version.Groups[1].Value}";
        }

        return metadata;
    }

    [GeneratedRegex(@"/v(\d+)(?:/|$)")]
    private static partial Regex VersionPattern();
}
