using System.Text.Json;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// Base for auth contract tests: schema readiness, a cookie-transparent
/// client, and assertions pinning the frozen response envelope.
/// </summary>
public abstract class AuthContractTest(AuthApiFactory factory) : IAsyncLifetime
{
    protected AuthApiFactory Factory { get; } = factory;

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

    protected async Task<(string Username, string Password, string Email, Guid Id)> SeedPatientAsync()
    {
        var username = Unique("patient");
        var password = "Patient-Passw0rd";
        var id = await Factory.SeedUserAsync(username, password, "patient", DateTimeOffset.UtcNow);
        return (username, password, $"{username}@contract.test", id);
    }

    protected async Task<(string Username, string Password, string Email, Guid Id)> SeedAdminAsync()
    {
        var username = Unique("admin");
        var password = "Admin-Passw0rd";
        var id = await Factory.SeedUserAsync(username, password, "admin", DateTimeOffset.UtcNow);
        return (username, password, $"{username}@contract.test", id);
    }

    protected Task<ApiResponse> LoginAsync(string username, string password) =>
        Api.PostJsonAsync(Client, "/v1/auth/login", new { username, password });

    /// <summary>Pins the frozen success-envelope metadata.</summary>
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
        AssertIsoTimestamp(metadata.GetProperty("timestamp").GetString());
    }

    /// <summary>Pins the frozen error-envelope metadata and stable issue.code.</summary>
    protected static JsonElement AssertErrorEnvelope(ApiResponse response, int statusCode, string expectedCode)
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

    private static void AssertIsoTimestamp(string? timestamp)
    {
        Assert.NotNull(timestamp);
        Assert.EndsWith("Z", timestamp, StringComparison.Ordinal);
        Assert.True(DateTimeOffset.TryParse(timestamp, out _), $"not ISO-8601: {timestamp}");
    }
}
