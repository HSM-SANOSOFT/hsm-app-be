using System.Net.Http.Headers;
using System.Text.Json;
using Hsm.Contract.Tests.Auth;

namespace Hsm.Contract.Tests.Docs;

/// <summary>
/// Base for the U15 docs contract tests: schema + bucket readiness, an
/// authenticated bearer (docs routes accept ANY authenticated, onboarded
/// role), multipart upload helpers, and the frozen-envelope assertions.
/// </summary>
public abstract class DocsContractTest(DocsApiFactory factory) : IAsyncLifetime
{
    protected DocsApiFactory Factory { get; } = factory;

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
        var username = Unique("docs");
        await Factory.SeedUserAsync(username, "Docs-Passw0rd", role, DateTimeOffset.UtcNow);
        var login = await Api.PostJsonAsync(
            Client, "/v1/auth/login", new { username, password = "Docs-Passw0rd" });
        return login.AccessToken;
    }

    protected async Task<ApiResponse> DeleteAsync(string path, string? bearer = null, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        if (body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await ApiResponse.FromAsync(await Client.SendAsync(request));
    }

    /// <summary>POSTs the frozen multipart shape: 'payload' JSON field + 'files' parts.</summary>
    protected async Task<ApiResponse> PostUploadAsync(
        string bearer,
        string? payloadJson,
        (string FileName, string ContentType, byte[] Content)[] files,
        (string Name, string Value)[]? extraFields = null,
        string filesFieldName = "files")
    {
        using var content = new MultipartFormDataContent();
        if (payloadJson is not null)
        {
            content.Add(new StringContent(payloadJson), "payload");
        }

        if (extraFields is not null)
        {
            foreach (var (name, value) in extraFields)
            {
                content.Add(new StringContent(value), name);
            }
        }

        foreach (var (fileName, contentType, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            content.Add(part, filesFieldName, fileName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/docs/upload") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await ApiResponse.FromAsync(await Client.SendAsync(request));
    }

    /// <summary>Uploads one file into <paramref name="folderName"/> and returns (documentId, fileId, key).</summary>
    protected async Task<(string DocumentId, string FileId, string Key)> UploadOneAsync(
        string bearer,
        string fileName,
        byte[] content,
        string folderName = "uploads",
        string? entityId = null,
        string? entityType = null)
    {
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                bucket = DocsApiFactory.Bucket,
                files = new[] { new { folderName, fileInfo = new { fileName } } },
            },
        });
        var extraFields = new List<(string, string)>();
        if (entityId is not null)
        {
            extraFields.Add(("entityId", entityId));
        }

        if (entityType is not null)
        {
            extraFields.Add(("entityType", entityType));
        }

        var response = await PostUploadAsync(
            bearer, payload, [(fileName, "text/plain", content)], [.. extraFields]);
        Assert.True(response.Status == 201, $"upload failed: {response.RawBody}");
        var file = response.Data.GetProperty("s3Result")[0].GetProperty("files")[0];
        return (
            response.Data.GetProperty("documentIds")[0].GetString()!,
            file.GetProperty("fileId").GetString()!,
            file.GetProperty("key").GetString()!);
    }

    /// <summary>Polls GET /v1/docs/{id} until the document reaches a terminal status.</summary>
    protected async Task<JsonElement> WaitForTerminalStatusAsync(
        string bearer, string documentId, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            var response = await Api.GetAsync(Client, $"/v1/docs/{documentId}", bearer: bearer);
            Assert.True(response.Status == 200, $"poll failed: {response.RawBody}");
            var status = response.Data.GetProperty("status").GetString();
            if (status is "COMPLETED" or "FAILED")
            {
                return response.Data;
            }

            Assert.True(
                DateTimeOffset.UtcNow < deadline,
                $"document {documentId} stuck in status '{status}'");
            await Task.Delay(100);
        }
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

    /// <summary>Pins the frozen buildPaginationMeta block on list responses.</summary>
    protected static void AssertPagination(
        ApiResponse response, int page, int pageSize, int totalItems, int totalPages)
    {
        var pagination = response.Metadata.GetProperty("extra").GetProperty("pagination");
        Assert.Equal(page, pagination.GetProperty("page").GetInt32());
        Assert.Equal(pageSize, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(totalItems, pagination.GetProperty("totalItems").GetInt32());
        Assert.Equal(totalPages, pagination.GetProperty("totalPages").GetInt32());
    }
}
