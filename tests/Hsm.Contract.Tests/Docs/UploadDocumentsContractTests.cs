using System.Text.Json;
using Hsm.Contract.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Docs;

/// <summary>
/// POST /v1/docs/upload — behavior pinned from the frozen
/// docs.service.uploadDocuments: filename-matched multipart parts, blob
/// upload under "&lt;folder-lower&gt;/&lt;uuid&gt;", one transaction for the
/// document + version 1 + storage (+ link) graph, { s3Result, documentIds }
/// back, and the frozen 500s for unmatched/extra files.
/// </summary>
public sealed class UploadDocumentsContractTests(DocsApiFactory factory)
    : DocsContractTest(factory), IClassFixture<DocsApiFactory>
{
    [Fact]
    public async Task Upload_requires_authentication()
    {
        var response = await Api.PostJsonAsync(Client, "/v1/docs/upload", new { });
        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Upload_stores_blob_version_and_document()
    {
        var bearer = await BearerAsync();
        var fileName = Unique("report") + ".txt";
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                bucket = DocsApiFactory.Bucket,
                files = new[] { new { folderName = "Uploads/Sub", fileInfo = new { fileName } } },
            },
        });

        var response = await PostUploadAsync(
            bearer, payload, [(fileName, "text/plain", "hello upload"u8.ToArray())]);
        AssertSuccessEnvelope(response, 201, "/v1/docs/upload");

        var bucketResult = response.Data.GetProperty("s3Result")[0];
        Assert.Equal(DocsApiFactory.Bucket, bucketResult.GetProperty("bucket").GetString());
        var file = bucketResult.GetProperty("files")[0];
        Assert.Equal(fileName, file.GetProperty("filename").GetString());
        var key = file.GetProperty("key").GetString()!;
        // Frozen key normalization: trimmed, lowercased folder + "/" + uuid.
        Assert.StartsWith("uploads/sub/", key, StringComparison.Ordinal);
        Assert.EndsWith(file.GetProperty("fileId").GetString()!, key, StringComparison.Ordinal);

        var documentId = response.Data.GetProperty("documentIds")[0].GetString()!;
        var document = await Api.GetAsync(Client, $"/v1/docs/{documentId}", bearer: bearer);
        AssertSuccessEnvelope(document, 200, $"/v1/docs/{documentId}");
        Assert.Equal(fileName, document.Data.GetProperty("title").GetString());
        Assert.Equal("UPLOADED", document.Data.GetProperty("type").GetString());
        Assert.Equal("COMPLETED", document.Data.GetProperty("status").GetString());
        Assert.Equal("MANUAL", document.Data.GetProperty("source").GetString());
        var version = document.Data.GetProperty("versions")[0];
        Assert.Equal(1, version.GetProperty("version").GetInt32());
        Assert.Equal(fileName, version.GetProperty("filename").GetString());
        Assert.Equal("text/plain", version.GetProperty("mimeType").GetString());
        Assert.Equal(12, version.GetProperty("size").GetInt64());
        Assert.Equal(key, version.GetProperty("storage").GetProperty("path").GetString());

        // The blob is real: presign it and read it back.
        using var http = new HttpClient();
        var url = await Api.GetAsync(Client, $"/v1/docs/{documentId}/url", bearer: bearer);
        Assert.Equal(
            "hello upload",
            await http.GetStringAsync(url.Data.GetProperty("url").GetString()));
    }

    [Fact]
    public async Task Zero_byte_upload_round_trips()
    {
        var bearer = await BearerAsync();
        var (documentId, _, _) = await UploadOneAsync(bearer, Unique("empty") + ".txt", []);

        var document = await Api.GetAsync(Client, $"/v1/docs/{documentId}", bearer: bearer);
        Assert.Equal(0, document.Data.GetProperty("versions")[0].GetProperty("size").GetInt64());

        var url = await Api.GetAsync(Client, $"/v1/docs/{documentId}/url", bearer: bearer);
        using var http = new HttpClient();
        using var fetched = await http.GetAsync(url.Data.GetProperty("url").GetString());
        Assert.Equal(System.Net.HttpStatusCode.OK, fetched.StatusCode);
        Assert.Empty(await fetched.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Duplicate_filenames_upload_one_blob_each()
    {
        var bearer = await BearerAsync();
        var fileName = Unique("dup") + ".txt";
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                bucket = DocsApiFactory.Bucket,
                files = new[]
                {
                    new { folderName = "dups", fileInfo = new { fileName } },
                    new { folderName = "dups", fileInfo = new { fileName } },
                },
            },
        });
        var response = await PostUploadAsync(bearer, payload,
        [
            (fileName, "text/plain", "one"u8.ToArray()),
            (fileName, "text/plain", "two"u8.ToArray()),
        ]);
        AssertSuccessEnvelope(response, 201, "/v1/docs/upload");
        Assert.Equal(2, response.Data.GetProperty("documentIds").GetArrayLength());
        var keys = response.Data.GetProperty("s3Result")[0].GetProperty("files")
            .EnumerateArray().Select(f => f.GetProperty("key").GetString()).ToList();
        Assert.Equal(2, keys.Distinct().Count());
    }

    [Fact]
    public async Task Unmatched_payload_filename_is_the_frozen_500()
    {
        var bearer = await BearerAsync();
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                bucket = DocsApiFactory.Bucket,
                files = new[] { new { folderName = "x", fileInfo = new { fileName = "missing.txt" } } },
            },
        });
        var response = await PostUploadAsync(bearer, payload, []);
        var issue = AssertErrorEnvelope(response, 500, "COMMON.INTERNAL");
        Assert.Contains(
            "No uploaded file matched payload filename=\"missing.txt\"",
            issue.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extra_file_not_in_payload_is_the_frozen_500()
    {
        var bearer = await BearerAsync();
        var response = await PostUploadAsync(
            bearer, "[]", [("extra.txt", "text/plain", "x"u8.ToArray())]);
        var issue = AssertErrorEnvelope(response, 500, "COMMON.INTERNAL");
        Assert.Contains(
            "Uploaded files not referenced in payload: extra.txt",
            issue.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_payload_field_is_a_validation_400()
    {
        var bearer = await BearerAsync();
        var response = await PostUploadAsync(
            bearer, payloadJson: null, [("a.txt", "text/plain", "x"u8.ToArray())]);
        AssertValidationFailure(response, "payload", "isArray");
    }

    [Fact]
    public async Task Nested_payload_validation_pins_the_frozen_constraint_paths()
    {
        var bearer = await BearerAsync();
        var payload = """[{"bucket":"","files":[{"folderName":"f","fileInfo":{}}]}]""";
        var response = await PostUploadAsync(bearer, payload, []);
        AssertValidationFailure(response, "payload.0.bucket", "isNotEmpty");
        AssertValidationFailure(response, "payload.0.files.0.fileInfo.fileName", "isNotEmpty");
    }

    [Fact]
    public async Task Malformed_payload_json_is_the_frozen_500()
    {
        var bearer = await BearerAsync();
        var response = await PostUploadAsync(bearer, "{not-json", []);
        AssertErrorEnvelope(response, 500, "COMMON.INTERNAL");
    }

    [Fact]
    public async Task Unknown_form_field_is_rejected_by_the_whitelist()
    {
        var bearer = await BearerAsync();
        var response = await PostUploadAsync(bearer, "[]", [], extraFields: [("rogue", "1")]);
        AssertValidationFailure(response, "rogue", "whitelistValidation");
    }

    [Fact]
    public async Task File_under_an_unexpected_field_is_the_frozen_multer_400()
    {
        var bearer = await BearerAsync();
        var response = await PostUploadAsync(
            bearer, "[]", [("a.txt", "text/plain", "x"u8.ToArray())], filesFieldName: "attachments");
        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal("Unexpected field", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Upload_with_entity_creates_a_link_but_not_document_columns()
    {
        var bearer = await BearerAsync();
        var entityId = Unique("hc");
        var (documentId, _, _) = await UploadOneAsync(
            bearer, Unique("linked") + ".txt", "linked"u8.ToArray(),
            entityId: entityId, entityType: "record");

        var links = await Factory.WithDbAsync(async db =>
            await db.DocumentLinks.Where(l => l.DocumentId == Guid.Parse(documentId)).ToListAsync());
        var link = Assert.Single(links);
        Assert.Equal(entityId, link.EntityId);
        Assert.Equal("record", link.EntityType);

        // Frozen: the document row's own entity columns stay null on upload.
        var document = await Api.GetAsync(Client, $"/v1/docs/{documentId}", bearer: bearer);
        Assert.Equal(JsonValueKind.Null, document.Data.GetProperty("entityId").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.Data.GetProperty("entityType").ValueKind);
    }
}
