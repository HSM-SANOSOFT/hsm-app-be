using System.Net.Http.Headers;
using System.Text.Json;

namespace Hsm.Contract.Tests.Docs;

/// <summary>
/// Base for the U15 docs contract tests: the shared plumbing plus multipart
/// upload helpers and the generation-status poll (docs routes accept ANY
/// authenticated, onboarded role).
/// </summary>
public abstract class DocsContractTest(DocsApiFactory factory) : ContractTest<DocsApiFactory>(factory)
{
    /// <summary>Seeds a doctor (any-role surface) and returns a bearer access token.</summary>
    protected async Task<string> BearerAsync(string role = "doctor") =>
        (await base.BearerAsync(role, onboarded: true)).Bearer;

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

    /// <summary>Pins the frozen buildPaginationMeta block on list responses.</summary>
    protected static void AssertPagination(
        ApiResponse response, int page, int pageSize, int totalItems, int totalPages) =>
        EnvelopeAssert.Pagination(response, page, pageSize, totalItems, totalPages);
}
