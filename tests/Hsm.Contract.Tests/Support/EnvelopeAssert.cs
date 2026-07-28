using System.Text.Json;

namespace Hsm.Contract.Tests;

/// <summary>
/// Assertions pinning the frozen response envelope
/// (response.interceptor.ts / response.filter.ts), shared by every suite —
/// including the ISO-8601 timestamp check, applied uniformly.
/// </summary>
public static class EnvelopeAssert
{
    /// <summary>Pins the frozen success-envelope metadata.</summary>
    public static void Success(ApiResponse response, int statusCode, string path)
    {
        Assert.True(
            statusCode == response.Status,
            $"expected {statusCode}, got {response.Status}: {response.RawBody}");
        var metadata = response.Metadata;
        Assert.True(metadata.GetProperty("success").GetBoolean());
        Assert.Equal(statusCode, metadata.GetProperty("statusCode").GetInt32());
        Assert.Equal(path, metadata.GetProperty("path").GetString());
        Assert.Equal("Request processed successfully.", metadata.GetProperty("message").GetString());
        Assert.Equal("v1", metadata.GetProperty("apiVersion").GetString());
        AssertIsoTimestamp(metadata.GetProperty("timestamp").GetString());
    }

    /// <summary>Pins the frozen error-envelope metadata and stable issue.code.</summary>
    public static JsonElement Error(ApiResponse response, int statusCode, string expectedCode)
    {
        Assert.True(
            statusCode == response.Status,
            $"expected {statusCode}, got {response.Status}: {response.RawBody}");
        var metadata = response.Metadata;
        Assert.False(metadata.GetProperty("success").GetBoolean());
        Assert.Equal(statusCode, metadata.GetProperty("statusCode").GetInt32());
        Assert.Equal("Request processed unsuccessfully.", metadata.GetProperty("message").GetString());
        AssertIsoTimestamp(metadata.GetProperty("timestamp").GetString());
        var issue = response.Issue;
        Assert.Equal(expectedCode, issue.GetProperty("code").GetString());
        return issue;
    }

    /// <summary>Asserts a COMMON.VALIDATION 400 carrying a constraint for a field.</summary>
    public static void Validation(ApiResponse response, string field, string constraint)
    {
        var issue = Error(response, 400, "COMMON.VALIDATION");
        var match = issue.GetProperty("errors").EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("field").GetString() == field);
        Assert.True(
            match.ValueKind == JsonValueKind.Object,
            $"no validation entry for field '{field}': {response.RawBody}");
        Assert.Contains(
            constraint,
            match.GetProperty("constraints").EnumerateArray().Select(c => c.GetString()));
    }

    /// <summary>Pins the frozen buildPaginationMeta block on list responses.</summary>
    public static void Pagination(
        ApiResponse response, int page, int pageSize, int totalItems, int totalPages)
    {
        var pagination = response.Metadata.GetProperty("extra").GetProperty("pagination");
        Assert.Equal(page, pagination.GetProperty("page").GetInt32());
        Assert.Equal(pageSize, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(totalItems, pagination.GetProperty("totalItems").GetInt32());
        Assert.Equal(totalPages, pagination.GetProperty("totalPages").GetInt32());
    }

    public static void AssertIsoTimestamp(string? timestamp)
    {
        Assert.NotNull(timestamp);
        Assert.EndsWith("Z", timestamp, StringComparison.Ordinal);
        Assert.True(DateTimeOffset.TryParse(timestamp, out _), $"not ISO-8601: {timestamp}");
    }
}
