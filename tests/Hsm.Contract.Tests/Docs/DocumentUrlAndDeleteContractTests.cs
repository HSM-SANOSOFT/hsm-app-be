using System.Net;
using Hsm.Contract.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Docs;

/// <summary>
/// GET /v1/docs/{id}, GET /v1/docs/{id}/url, POST /v1/docs/url, and
/// DELETE /v1/docs/{id} — behavior pinned from the frozen docs.service:
/// latest-version presigning (inline), the bulk presign pass-through with
/// contentDisposition/expiresInSeconds, owner-scoped 404s, the missing
/// ParseUUIDPipe (malformed ids reached PostgreSQL and 500'd), and the
/// deletion semantics: soft delete + best-effort blob removal, links
/// SURVIVING (neither reject nor cascade).
/// </summary>
public sealed class DocumentUrlAndDeleteContractTests(DocsApiFactory factory)
    : DocsContractTest(factory), IClassFixture<DocsApiFactory>
{
    [Fact]
    public async Task Routes_require_authentication()
    {
        AssertErrorEnvelope(
            await Api.GetAsync(Client, $"/v1/docs/{Guid.NewGuid()}"), 401, "COMMON.UNAUTHORIZED");
        AssertErrorEnvelope(
            await Api.GetAsync(Client, $"/v1/docs/{Guid.NewGuid()}/url"), 401, "COMMON.UNAUTHORIZED");
        AssertErrorEnvelope(
            await Api.PostJsonAsync(Client, "/v1/docs/url", new { }), 401, "COMMON.UNAUTHORIZED");
        AssertErrorEnvelope(
            await DeleteAsync($"/v1/docs/{Guid.NewGuid()}"), 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Unknown_document_is_the_frozen_404()
    {
        var bearer = await BearerAsync();
        var id = Guid.NewGuid();
        var issue = AssertErrorEnvelope(
            await Api.GetAsync(Client, $"/v1/docs/{id}", bearer: bearer), 404, "COMMON.NOT_FOUND");
        Assert.Equal($"Document '{id}' not found", issue.GetProperty("message").GetString());
        AssertErrorEnvelope(
            await Api.GetAsync(Client, $"/v1/docs/{id}/url", bearer: bearer), 404, "COMMON.NOT_FOUND");
        AssertErrorEnvelope(
            await DeleteAsync($"/v1/docs/{id}", bearer: bearer), 404, "COMMON.NOT_FOUND");
    }

    [Fact]
    public async Task Malformed_id_is_the_frozen_500()
    {
        // The frozen docs routes had no ParseUUIDPipe — the malformed id
        // reached PostgreSQL and its uuid-cast failure surfaced as a 500.
        var bearer = await BearerAsync();
        AssertErrorEnvelope(
            await Api.GetAsync(Client, "/v1/docs/not-a-uuid", bearer: bearer), 500, "COMMON.INTERNAL");
        AssertErrorEnvelope(
            await Api.GetAsync(Client, "/v1/docs/not-a-uuid/url", bearer: bearer), 500, "COMMON.INTERNAL");
        AssertErrorEnvelope(
            await DeleteAsync("/v1/docs/not-a-uuid", bearer: bearer), 500, "COMMON.INTERNAL");
    }

    [Fact]
    public async Task Document_without_a_stored_file_is_the_frozen_404()
    {
        var bearer = await BearerAsync();
        var created = await Api.PostJsonAsync(Client, "/v1/docs/generate", new
        {
            templateIdentifier = Unique("absent"),
            data = new { },
            title = "Nunca genera",
        }, bearer: bearer);
        var documentId = created.Data.GetProperty("documentId").GetString()!;
        await WaitForTerminalStatusAsync(bearer, documentId);

        var issue = AssertErrorEnvelope(
            await Api.GetAsync(Client, $"/v1/docs/{documentId}/url", bearer: bearer),
            404, "COMMON.NOT_FOUND");
        Assert.Equal(
            $"No generated file found for document '{documentId}'",
            issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Bulk_presign_grants_and_expiry_is_enforced()
    {
        var bearer = await BearerAsync();
        var (_, fileId, _) = await UploadOneAsync(
            bearer, Unique("presign") + ".txt", "presign me"u8.ToArray(), folderName: "presign");

        var response = await Api.PostJsonAsync(
            Client,
            "/v1/docs/url?contentDisposition=attachment&expiresInSeconds=1",
            new
            {
                documents = new[]
                {
                    new
                    {
                        bucket = DocsApiFactory.Bucket,
                        files = new[] { new { folderName = "presign", fileInfo = new { fileId } } },
                    },
                },
            },
            bearer: bearer);
        AssertSuccessEnvelope(
            response, 201, "/v1/docs/url?contentDisposition=attachment&expiresInSeconds=1");

        var bucketResult = response.Data[0];
        Assert.Equal(DocsApiFactory.Bucket, bucketResult.GetProperty("bucket").GetString());
        var file = bucketResult.GetProperty("files")[0];
        Assert.Equal(fileId, file.GetProperty("fileId").GetString());
        Assert.Equal($"presign/{fileId}", file.GetProperty("key").GetString());

        using var http = new HttpClient();
        using var granted = await http.GetAsync(file.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal("presign me", await granted.Content.ReadAsStringAsync());
        Assert.Equal(
            "attachment", granted.Content.Headers.ContentDisposition?.DispositionType);

        // Frozen presigned URLs die on schedule — the grant is time-limited.
        await Task.Delay(TimeSpan.FromSeconds(3));
        using var expired = await http.GetAsync(file.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
    }

    [Fact]
    public async Task Bulk_presign_accepts_an_empty_documents_array()
    {
        var bearer = await BearerAsync();
        var response = await Api.PostJsonAsync(
            Client, "/v1/docs/url", new { documents = Array.Empty<object>() }, bearer: bearer);
        AssertSuccessEnvelope(response, 201, "/v1/docs/url");
        Assert.Empty(response.Data.EnumerateArray());
    }

    [Fact]
    public async Task Bulk_presign_validation_pins_the_frozen_constraints()
    {
        var bearer = await BearerAsync();
        AssertValidationFailure(
            await Api.PostJsonAsync(Client, "/v1/docs/url", new { }, bearer: bearer),
            "documents", "isArray");

        var nested = await Api.PostJsonAsync(Client, "/v1/docs/url", new
        {
            documents = new[]
            {
                new { bucket = "", files = new[] { new { folderName = "f", fileInfo = new { } } } },
            },
        }, bearer: bearer);
        AssertValidationFailure(nested, "documents.0.bucket", "isNotEmpty");
        AssertValidationFailure(nested, "documents.0.files.0.fileInfo.fileId", "isNotEmpty");
    }

    [Fact]
    public async Task Delete_soft_deletes_the_row_and_removes_the_blob()
    {
        var bearer = await BearerAsync();
        var (documentId, fileId, _) = await UploadOneAsync(
            bearer, Unique("gone") + ".txt", "delete me"u8.ToArray(), folderName: "gone");

        var deleted = await DeleteAsync($"/v1/docs/{documentId}", bearer: bearer);
        AssertSuccessEnvelope(deleted, 200, $"/v1/docs/{documentId}");
        Assert.True(deleted.Data.GetProperty("deleted").GetBoolean());

        // Soft delete: 404 through the API, row present with deletedAt set.
        AssertErrorEnvelope(
            await Api.GetAsync(Client, $"/v1/docs/{documentId}", bearer: bearer),
            404, "COMMON.NOT_FOUND");
        var row = await Factory.WithDbAsync(async db =>
            await db.Documents.SingleAsync(d => d.Id == Guid.Parse(documentId)));
        Assert.NotNull(row.DeletedAt);

        // The blob is gone: a fresh presigned URL for it now 404s.
        var presign = await Api.PostJsonAsync(Client, "/v1/docs/url", new
        {
            documents = new[]
            {
                new
                {
                    bucket = DocsApiFactory.Bucket,
                    files = new[] { new { folderName = "gone", fileInfo = new { fileId } } },
                },
            },
        }, bearer: bearer);
        using var http = new HttpClient();
        using var fetched = await http.GetAsync(
            presign.Data[0].GetProperty("files")[0].GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);

        // Frozen: deleting again is a 404 (soft-deleted rows are invisible).
        AssertErrorEnvelope(
            await DeleteAsync($"/v1/docs/{documentId}", bearer: bearer), 404, "COMMON.NOT_FOUND");
    }

    [Fact]
    public async Task Deleting_a_linked_document_succeeds_and_the_link_survives()
    {
        // Frozen semantics pinned: a linked document is neither protected
        // nor cascaded — the delete succeeds and the link rows remain.
        var bearer = await BearerAsync();
        var (documentId, _, _) = await UploadOneAsync(
            bearer, Unique("linked") + ".txt", "linked"u8.ToArray(),
            entityId: Unique("ent"), entityType: "record");

        var deleted = await DeleteAsync($"/v1/docs/{documentId}", bearer: bearer);
        AssertSuccessEnvelope(deleted, 200, $"/v1/docs/{documentId}");

        var links = await Factory.WithDbAsync(async db =>
            await db.DocumentLinks.CountAsync(l => l.DocumentId == Guid.Parse(documentId)));
        Assert.Equal(1, links);
    }

    [Fact]
    public async Task Delete_scopes_by_owner()
    {
        var owner = await BearerAsync();
        var other = await BearerAsync();
        var (documentId, _, _) = await UploadOneAsync(owner, Unique("mine") + ".txt", "mine"u8.ToArray());
        AssertErrorEnvelope(
            await DeleteAsync($"/v1/docs/{documentId}", bearer: other), 404, "COMMON.NOT_FOUND");
    }
}
