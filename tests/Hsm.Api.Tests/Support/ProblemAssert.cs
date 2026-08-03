using System.Text.Json;

namespace Hsm.Api.Tests;

/// <summary>
/// Every error assertion in this project goes through here, so the invariant
/// "problem+json, with status and traceId, on every failure" is stated once.
/// </summary>
public static class ProblemAssert
{
    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, int expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        var problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal(expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.True(problem.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
        return problem;
    }
}
