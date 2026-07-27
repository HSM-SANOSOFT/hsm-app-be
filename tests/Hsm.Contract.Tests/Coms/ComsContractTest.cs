using System.Text.Json;
using Hsm.Contract.Tests.Auth;

namespace Hsm.Contract.Tests.Coms;

/// <summary>
/// Base for the U14 coms contract tests: schema readiness, bearer login (any
/// authenticated onboarded role), envelope assertions, and a poll helper for
/// the background dispatcher's observable outcomes.
/// </summary>
public abstract class ComsContractTest(ComsApiFactory factory) : IAsyncLifetime
{
    protected ComsApiFactory Factory { get; } = factory;

    protected HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Factory.EnsureSchemaAsync();
        Client = Factory.CreateApiClient();
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    protected static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    protected async Task<(string Bearer, Guid UserId)> BearerAsync(string role = "doctor")
    {
        var username = Unique("coms");
        var id = await Factory.SeedUserAsync(username, "Coms-Passw0rd", role);
        var login = await Api.PostJsonAsync(
            Client, "/v1/auth/login", new { username, password = "Coms-Passw0rd" });
        return (login.AccessToken, id);
    }

    /// <summary>Polls until the condition holds (the in-process dispatcher is asynchronous).</summary>
    protected static async Task WaitForAsync(Func<Task<bool>> condition, string what, int timeoutMs = 10000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Timed out waiting for: {what}");
    }

    protected async Task<JsonElement> GetBatchAsync(string bearer, string batchId)
    {
        var response = await Api.GetAsync(Client, $"/v1/coms/emails/batches/{batchId}", bearer: bearer);
        Assert.True(response.Status == 200, $"get batch failed: {response.RawBody}");
        return response.Data;
    }

    protected static void AssertSuccessEnvelope(ApiResponse response, int statusCode, string path)
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
    }

    protected static JsonElement AssertErrorEnvelope(ApiResponse response, int statusCode, string expectedCode)
    {
        Assert.True(
            statusCode == response.Status,
            $"expected {statusCode}, got {response.Status}: {response.RawBody}");
        var metadata = response.Metadata;
        Assert.False(metadata.GetProperty("success").GetBoolean());
        Assert.Equal(statusCode, metadata.GetProperty("statusCode").GetInt32());
        Assert.Equal("Request processed unsuccessfully.", metadata.GetProperty("message").GetString());
        var issue = response.Issue;
        Assert.Equal(expectedCode, issue.GetProperty("code").GetString());
        return issue;
    }

    protected static void AssertValidationFailure(ApiResponse response, string field, string constraint)
    {
        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        var match = issue.GetProperty("errors").EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("field").GetString() == field);
        Assert.True(
            match.ValueKind == JsonValueKind.Object,
            $"no validation entry for field '{field}': {response.RawBody}");
        Assert.Contains(
            constraint,
            match.GetProperty("constraints").EnumerateArray().Select(c => c.GetString()));
    }
}
