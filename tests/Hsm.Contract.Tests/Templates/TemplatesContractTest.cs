using System.Text.Json;
using Hsm.Contract.Tests.Auth;

namespace Hsm.Contract.Tests.Templates;

/// <summary>
/// Base for the U14 template contract tests: schema readiness, an
/// authenticated bearer (templates routes accept ANY authenticated, onboarded
/// role), and the frozen-envelope assertions.
/// </summary>
public abstract class TemplatesContractTest(TemplatesApiFactory factory) : IAsyncLifetime
{
    protected TemplatesApiFactory Factory { get; } = factory;

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

    /// <summary>Seeds a doctor (any-role surface) and returns a bearer access token.</summary>
    protected async Task<string> BearerAsync(string role = "doctor")
    {
        var username = Unique("tmpl");
        await Factory.SeedUserAsync(username, "Tmpl-Passw0rd", role, DateTimeOffset.UtcNow);
        var login = await Api.PostJsonAsync(
            Client, "/v1/auth/login", new { username, password = "Tmpl-Passw0rd" });
        return login.AccessToken;
    }

    /// <summary>Creates a BASE template through the API and returns its id.</summary>
    protected async Task<string> CreateBaseAsync(string bearer, string? name = null)
    {
        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "BASE",
            name = name ?? Unique("base"),
            schema = new { },
            content = "<html><body>{{{body}}}</body></html>",
        }, bearer: bearer);
        Assert.True(response.Status == 201, $"base create failed: {response.RawBody}");
        return response.Data.GetProperty("template").GetProperty("id").GetString()!;
    }

    /// <summary>Creates an EMAIL_INTERNAL template through the API and returns its id.</summary>
    protected async Task<string> CreateEmailTemplateAsync(
        string bearer, string baseId, string? name = null, object? schema = null)
    {
        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "EMAIL_INTERNAL",
            name = name ?? Unique("email"),
            schema = schema ?? new { patientName = "string" },
            content = "<p>Hola {{patientName}}</p>",
            baseTemplateId = baseId,
            email = new
            {
                subject = "Hola {{patientName}}",
                fromEmail = "no-reply@hsm.test",
                fromName = "HSM",
            },
        }, bearer: bearer);
        Assert.True(response.Status == 201, $"email template create failed: {response.RawBody}");
        return response.Data.GetProperty("template").GetProperty("id").GetString()!;
    }

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
        var issue = response.Issue;
        Assert.Equal(expectedCode, issue.GetProperty("code").GetString());
        return issue;
    }

    /// <summary>Asserts a COMMON.VALIDATION 400 carrying a constraint for a field.</summary>
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

    /// <summary>Pins the frozen synthesized pagination for bare-array list payloads.</summary>
    protected static void AssertSyntheticPagination(ApiResponse response, int count)
    {
        var pagination = response.Metadata.GetProperty("extra").GetProperty("pagination");
        Assert.Equal(1, pagination.GetProperty("page").GetInt32());
        Assert.Equal(count, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(count, pagination.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, pagination.GetProperty("totalPages").GetInt32());
    }
}
