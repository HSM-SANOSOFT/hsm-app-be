using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Documents;

/// <summary>
/// The one factory in this assembly that also CONSUMES what it enqueues.
///
/// <para>Its own database, so the job consumer this suite starts cannot reach
/// into <see cref="DocumentsFactory"/>'s data, and its own queue namespace
/// (<c>ApiHostFactory</c> mints one per factory instance) so the two suites'
/// streams stay separate.</para>
/// </summary>
public sealed class DocumentBlobCleanupFactory : DocumentsFactory
{
    protected override string DatabaseName => "hsm_api_tests_document_blob_cleanup";

    protected override bool ConsumesJobs => true;
}

/// <summary>
/// Deleting a document has to delete its BYTES, and until now nothing in the
/// suite said so.
///
/// <para><c>DeleteDocumentHandler</c> deliberately does not touch object storage
/// — it captures the blobs' coordinates and the endpoint enqueues
/// <c>DeleteDocumentBlobsCommand</c> once the deleting transaction has
/// committed. Every other documents test leaves <c>ConsumesJobs</c> false, so
/// they prove the row is gone and stop there: deleting the
/// <c>queue.EnqueueAsync</c> call outright would have left all of them green
/// while every blob leaked forever. This suite runs a real consumer against
/// real RustFS and asserts the object itself is unreachable afterwards, on both
/// the single and the bulk delete route.</para>
///
/// <para>The object is checked through a presigned URL taken BEFORE the delete
/// rather than through <c>IObjectStorage</c> directly. The URL's signature
/// outlives the object, so a 404 from it is the store answering "no such key",
/// which is exactly the claim — and it keeps the assertion on the same public
/// surface the rest of the suite uses.</para>
/// </summary>
public class DocumentBlobCleanupTests(DocumentBlobCleanupFactory factory)
    : IClassFixture<DocumentBlobCleanupFactory>, IAsyncLifetime
{
    private const string FileContent = "cleanup me";

    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deleting_one_document_eventually_deletes_its_blob()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var id = await UploadAsync(client);
        var url = await PresignedUrlAsync(client, id);
        // The object is really there first, so "gone" later cannot be explained
        // by it never having existed.
        Assert.Equal(FileContent, await FetchAsync(url));

        var deleted = await client.DeleteAsync($"/api/v1/documents/{id}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await AssertBlobEventuallyGoneAsync(url);
    }

    [Fact]
    public async Task Bulk_deleting_documents_eventually_deletes_every_blob()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var firstId = await UploadAsync(client);
        var secondId = await UploadAsync(client);
        var firstUrl = await PresignedUrlAsync(client, firstId);
        var secondUrl = await PresignedUrlAsync(client, secondId);
        Assert.Equal(FileContent, await FetchAsync(firstUrl));
        Assert.Equal(FileContent, await FetchAsync(secondUrl));

        var deleted = await client.DeleteAsync(
            $"/api/v1/documents?ids={firstId},{secondId}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await AssertBlobEventuallyGoneAsync(firstUrl);
        await AssertBlobEventuallyGoneAsync(secondUrl);
    }

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        var filename = $"cleanup-{Guid.NewGuid():N}.txt";
        var payload = new[]
        {
            new
            {
                bucket = DocumentsFactory.Bucket,
                files = new[] { new { folderName = "docs", fileName = filename } },
            },
        };
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(FileContent));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "files", filename);
        content.Add(new StringContent(JsonSerializer.Serialize(payload)), "payload");

        var response = await client.PostAsync("/api/v1/documents", content, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        return body.GetProperty("documentIds")[0].GetGuid();
    }

    private static async Task<string> PresignedUrlAsync(HttpClient client, Guid id)
    {
        var body = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/documents/{id}/url", CancellationToken.None);
        var url = body.GetProperty("url").GetString();
        Assert.False(string.IsNullOrWhiteSpace(url));
        return url!;
    }

    private static async Task<string> FetchAsync(string url)
    {
        using var raw = new HttpClient();
        return await raw.GetStringAsync(url, CancellationToken.None);
    }

    /// <summary>
    /// Polls until the store stops serving the object. The delete is
    /// asynchronous by design — the 204 is answered before the worker has run —
    /// so the assertion has to be "eventually", with a deadline generous enough
    /// that a slow consumer reads as slow rather than as broken.
    /// </summary>
    private static async Task AssertBlobEventuallyGoneAsync(string url)
    {
        using var raw = new HttpClient();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await raw.GetAsync(url, CancellationToken.None);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                return;
            }

            await Task.Delay(100, CancellationToken.None);
        }

        Assert.Fail(
            "The blob was still served 30s after the document was deleted — the cleanup job was "
            + "never enqueued, or never ran.");
    }
}
