using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
        var issue = new JsonObject();
        if (exception is ApiValidationException validation)
        {
            issue["code"] = ApiErrorCode.Validation;
            var messages = new JsonArray();
            foreach (var message in validation.Messages)
            {
                messages.Add(message);
            }

            issue["message"] = messages;
            var errors = new JsonArray();
            foreach (var (field, constraints) in validation.FieldConstraints)
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

    /// <summary>The frozen status→code fallback map (response.filter.ts).</summary>
    private static string CodeForStatus(int status) => status switch
    {
        401 => ApiErrorCode.Unauthorized,
        403 => ApiErrorCode.Forbidden,
        404 => ApiErrorCode.NotFound,
        409 => ApiErrorCode.Conflict,
        429 => ApiErrorCode.TooManyRequests,
        400 or 422 => ApiErrorCode.Validation,
        _ => ApiErrorCode.Internal,
    };

    private static JsonObject Metadata(HttpContext ctx, int statusCode, bool success)
    {
        var path = ctx.Request.Path + ctx.Request.QueryString;
        var metadata = new JsonObject
        {
            ["success"] = success,
            ["statusCode"] = statusCode,
            ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
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
