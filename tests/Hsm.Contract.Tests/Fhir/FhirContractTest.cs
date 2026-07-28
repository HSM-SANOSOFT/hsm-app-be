using System.Text.Json;
using Hsm.Contract.Tests.Auth;

namespace Hsm.Contract.Tests.Fhir;

/// <summary>
/// Base for the U16 FHIR contract tests. The assertions pin the frozen FHIR
/// posture: success responses are the RAW resource/Bundle (no envelope),
/// errors are FHIR OperationOutcome bodies typed application/fhir+json with
/// the frozen status→issue-code map.
/// </summary>
public abstract class FhirContractTest(FhirApiFactory factory) : IAsyncLifetime
{
    protected FhirApiFactory Factory { get; } = factory;

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

    /// <summary>Seeds a user with <paramref name="role"/> and returns a bearer access token.</summary>
    protected async Task<string> BearerAsync(
        string role = "doctor", bool onboarded = true)
    {
        var username = Unique("fhir");
        await Factory.SeedUserAsync(
            username, "Fhir-Passw0rd", role, onboarded ? DateTimeOffset.UtcNow : null);
        var login = await Api.PostJsonAsync(
            Client, "/v1/auth/login", new { username, password = "Fhir-Passw0rd" });
        Assert.True(login.Status == 201, $"login failed: {login.RawBody}");
        return login.AccessToken;
    }

    /// <summary>Provisions an integration account and returns its bearer access token.</summary>
    protected async Task<string> IntegrationBearerAsync()
    {
        var admin = await BearerAsync("admin");
        var response = await Api.PostJsonAsync(
            Client,
            "/v1/auth/signup/integration",
            new { name = Unique("machine"), description = "fhir contract consumer", functionality = "dev" },
            bearer: admin);
        Assert.True(response.Status == 201, $"integration provisioning failed: {response.RawBody}");
        return response.AccessToken;
    }

    /// <summary>Pins a raw (non-enveloped) FHIR success response.</summary>
    protected static JsonElement AssertFhirResource(
        ApiResponse response, int statusCode, string resourceType)
    {
        Assert.True(
            statusCode == response.Status,
            $"expected {statusCode}, got {response.Status}: {response.RawBody}");
        // The frozen success bypassed the envelope AND kept Express's plain
        // application/json content type.
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        var root = response.Root;
        Assert.Equal(resourceType, root.GetProperty("resourceType").GetString());
        Assert.False(root.TryGetProperty("metadata", out _), "FHIR responses must not be enveloped");
        Assert.False(root.TryGetProperty("data", out _), "FHIR responses must not be enveloped");
        return root;
    }

    /// <summary>
    /// Pins the frozen OperationOutcome error body: application/fhir+json, one
    /// issue, severity error (fatal at 5xx), the status-mapped code. Returns
    /// the diagnostics text.
    /// </summary>
    protected static string AssertOperationOutcome(
        ApiResponse response, int statusCode, string issueCode)
    {
        Assert.True(
            statusCode == response.Status,
            $"expected {statusCode}, got {response.Status}: {response.RawBody}");
        Assert.StartsWith("application/fhir+json", response.ContentType, StringComparison.Ordinal);
        var root = response.Root;
        Assert.Equal("OperationOutcome", root.GetProperty("resourceType").GetString());
        Assert.False(root.TryGetProperty("metadata", out _), "FHIR errors must not be enveloped");
        var issue = Assert.Single(root.GetProperty("issue").EnumerateArray());
        Assert.Equal(statusCode >= 500 ? "fatal" : "error", issue.GetProperty("severity").GetString());
        Assert.Equal(issueCode, issue.GetProperty("code").GetString());
        return issue.GetProperty("diagnostics").GetString()!;
    }
}
