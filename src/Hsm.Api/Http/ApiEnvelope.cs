using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Hsm.Application;

namespace Hsm.Api.Http;

/// <summary>
/// The frozen response envelope (response.interceptor.ts), surviving here for
/// the eleven /v1/auth routes and any module route Tasks 5-10 have not yet
/// reshaped: success wraps payloads as { metadata, data }. The error half —
/// IssueFor/WriteErrorAsync/ValidationIssue/CodeForStatus — is gone; failures
/// render RFC 9457 problem+json through <see cref="Hsm.Api.Errors.HsmExceptionHandler"/>
/// now. Task 13 deletes this type once the surviving routes reshape.
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
