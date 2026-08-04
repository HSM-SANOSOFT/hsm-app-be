using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Documents;

public class DocumentsEndpointTests(DocumentsFactory factory) : IClassFixture<DocumentsFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private const string FileContent = "hello document";

    /// <summary>Uploads one small text document and returns the created document's id and its full response body.</summary>
    private static async Task<(Guid DocumentId, JsonElement Body)> UploadDocumentAsync(
        HttpClient client, string? entityId = null, string? entityType = null)
    {
        var filename = $"note-{Guid.NewGuid():N}.txt";
        var payload = new[]
        {
            new
            {
                bucket = DocumentsFactory.Bucket,
                files = new[] { new { folderName = "docs", fileName = filename } },
            },
        };
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(FileContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "files", filename);
        content.Add(new StringContent(JsonSerializer.Serialize(payload)), "payload");
        if (entityId is not null)
        {
            content.Add(new StringContent(entityId), "entityId");
        }

        if (entityType is not null)
        {
            content.Add(new StringContent(entityType), "entityType");
        }

        var response = await client.PostAsync("/api/v1/documents", content, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var documentId = body.GetProperty("documentIds")[0].GetGuid();
        return (documentId, body);
    }

    [Fact]
    public async Task Uploading_a_document_streams_the_file_into_storage_and_persists_its_version()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var (id, uploadBody) = await UploadDocumentAsync(client);
        var uploadedFile = uploadBody.GetProperty("items")[0].GetProperty("files")[0];
        Assert.Equal(DocumentsFactory.Bucket, uploadBody.GetProperty("items")[0].GetProperty("bucket").GetString());
        Assert.False(string.IsNullOrWhiteSpace(uploadedFile.GetProperty("fileId").GetString()));

        // Re-read via a second call rather than trusting the upload response —
        // proves the document and its version row actually persisted.
        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{id}", CancellationToken.None);

        Assert.Equal("UPLOADED", fetched.GetProperty("type").GetString());
        Assert.Equal("COMPLETED", fetched.GetProperty("status").GetString());
        var versions = fetched.GetProperty("versions");
        Assert.Equal(1, versions.GetArrayLength());
        Assert.Equal("text/plain", versions[0].GetProperty("contentType").GetString());
        Assert.Equal(FileContent.Length, versions[0].GetProperty("size").GetInt64());
        Assert.StartsWith("docs/", versions[0].GetProperty("key").GetString(), StringComparison.Ordinal);

        // Proves the bytes themselves made the round trip through
        // OpenReadStream() into the object store, not merely their length —
        // the module notes' streaming requirement has an observable outcome.
        var urlBody = await client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{id}/url", CancellationToken.None);
        using var raw = new HttpClient();
        var stored = await raw.GetStringAsync(urlBody.GetProperty("url").GetString());
        Assert.Equal(FileContent, stored);
    }

    [Fact]
    public async Task Generating_a_document_returns_202_with_a_location_header_and_ids()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.PostAsJsonAsync(
            "/api/v1/documents/generated",
            new
            {
                templateIdentifier = "tpl-not-rendered-in-this-suite",
                data = new { },
                title = "Generated title",
                description = (string?)null,
                entityId = (string?)null,
                entityType = (string?)null,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var id = accepted.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/documents/{id}", response.Headers.Location?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(accepted.GetProperty("jobId").GetString()));

        // The render job is never consumed in this suite (ConsumesJobs is
        // false, matching Task 6's EmailsFactory) — so the document must
        // still read back exactly as GenerateDocumentHandler left it.
        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{id}", CancellationToken.None);
        Assert.Equal("GENERATED", fetched.GetProperty("type").GetString());
        Assert.Equal("PENDING", fetched.GetProperty("status").GetString());
        Assert.Equal("Generated title", fetched.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Generating_a_document_with_no_title_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.PostAsJsonAsync(
            "/api/v1/documents/generated",
            new
            {
                templateIdentifier = "tpl-x",
                data = new { },
                title = string.Empty,
                description = (string?)null,
                entityId = (string?)null,
                entityType = (string?)null,
            },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("title", out _));
    }

    [Fact]
    public async Task List_is_paged_and_reports_its_totals()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        await UploadDocumentAsync(client);
        await UploadDocumentAsync(client);

        var response = await client.GetAsync("/api/v1/documents?page=1&pageSize=1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(1, page.GetProperty("pageSize").GetInt32());
        Assert.True(page.GetProperty("totalItems").GetInt32() >= 2);
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_is_scoped_to_the_calling_user()
    {
        using var owner = await factory.AuthenticatedClientAsync(Roles.Nurse);
        using var other = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var (id, _) = await UploadDocumentAsync(owner);

        var response = await other.GetAsync("/api/v1/documents", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.DoesNotContain(
            page.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Getting_an_unknown_document_id_is_a_404_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    [Fact]
    public async Task A_non_guid_id_is_an_unmatched_route()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.GetAsync("/api/v1/documents/not-a-guid", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Deleting_a_document_returns_204_and_the_document_is_actually_gone()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var (id, _) = await UploadDocumentAsync(client);

        var response = await client.DeleteAsync($"/api/v1/documents/{id}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var fetched = await client.GetAsync($"/api/v1/documents/{id}", CancellationToken.None);
        await ProblemAssert.ProblemAsync(fetched, 404);
    }

    [Fact]
    public async Task Deleting_an_unknown_document_id_is_a_404_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.DeleteAsync($"/api/v1/documents/{Guid.NewGuid()}", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    [Fact]
    public async Task Bulk_delete_removes_every_id_and_answers_204()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var (firstId, _) = await UploadDocumentAsync(client);
        var (secondId, _) = await UploadDocumentAsync(client);

        var response = await client.DeleteAsync(
            $"/api/v1/documents?ids={firstId},{secondId}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await ProblemAssert.ProblemAsync(
            await client.GetAsync($"/api/v1/documents/{firstId}", CancellationToken.None), 404);
        await ProblemAssert.ProblemAsync(
            await client.GetAsync($"/api/v1/documents/{secondId}", CancellationToken.None), 404);
    }

    [Fact]
    public async Task Bulk_delete_with_an_unknown_id_rolls_back_the_whole_batch()
    {
        // Regression for the ambient-transaction fix: without
        // DeleteDocumentsBulk wrapping every per-id Send in one
        // IUnitOfWork.ExecuteInTransactionAsync call, survivorId's delete
        // would commit on its own Send before the unknown id's
        // NotFoundException ever throws, and this test would fail — survivor
        // would already be gone by the time it re-fetches below.
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var (survivorId, _) = await UploadDocumentAsync(client);

        var response = await client.DeleteAsync(
            $"/api/v1/documents?ids={survivorId},{Guid.NewGuid()}", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);

        var fetched = await client.GetAsync($"/api/v1/documents/{survivorId}", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task Bulk_delete_with_a_malformed_id_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.DeleteAsync("/api/v1/documents?ids=not-a-guid", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("ids", out _));
    }

    [Fact]
    public async Task Presigning_urls_returns_a_working_url_for_every_requested_file()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var (_, uploadBody) = await UploadDocumentAsync(client);
        var uploadedItem = uploadBody.GetProperty("items")[0];
        var bucket = uploadedItem.GetProperty("bucket").GetString();
        var fileId = uploadedItem.GetProperty("files")[0].GetProperty("fileId").GetString();

        var response = await client.PostAsJsonAsync(
            "/api/v1/documents/urls",
            new
            {
                items = new[] { new { bucket, files = new[] { new { folderName = "docs", fileId } } } },
                contentDisposition = (string?)null,
                expiresInSeconds = (int?)null,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var url = results[0].GetProperty("files")[0].GetProperty("url").GetString();
        Assert.False(string.IsNullOrWhiteSpace(url));

        using var raw = new HttpClient();
        var stored = await raw.GetStringAsync(url);
        Assert.Equal(FileContent, stored);
    }

    [Fact]
    public async Task Uploading_with_an_unexpected_multipart_field_name_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("x"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "attachment", "note.txt");

        var response = await client.PostAsync("/api/v1/documents", content, CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("files", out _));
    }

    [Fact]
    public async Task Anonymous_upload_is_a_401_problem()
    {
        // Kept separate from the generic anonymous theory below: the upload
        // endpoint reads the multipart form before dispatching, so it needs a
        // well-formed multipart body to reach AuthorizationBehavior at all —
        // a JsonContent body (as every other route in the theory uses) would
        // fail form-parsing before authorization is ever checked.
        using var client = factory.CreateApiClient();
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("x"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "files", "note.txt");

        var response = await client.PostAsync("/api/v1/documents", content, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Theory]
    [InlineData("GET", "/api/v1/documents")]
    [InlineData("POST", "/api/v1/documents/generated")]
    [InlineData("POST", "/api/v1/documents/urls")]
    [InlineData("GET", "/api/v1/documents/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/v1/documents/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/v1/documents?ids=00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/v1/documents/00000000-0000-0000-0000-000000000001/url")]
    public async Task Anonymous_callers_get_401_on_every_other_documents_route(string method, string route)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { }),
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }
}
