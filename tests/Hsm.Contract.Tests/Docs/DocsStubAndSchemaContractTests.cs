using Hsm.Application.Docs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Docs;

/// <summary>
/// POST /v1/docs/create and DELETE /v1/docs (bulk) — pinned as the STUBS the
/// frozen controller shipped: createDocuments had an empty body and the bulk
/// delete ignored its payload without ever calling the service, both
/// answering the bare success envelope (no data key). Plus the U15 DoD
/// schema-inspection proof that no document table can hold a binary, and the
/// store-level guarantee that a second version preserves the first.
/// </summary>
public sealed class DocsStubAndSchemaContractTests(DocsApiFactory factory)
    : DocsContractTest(factory), IClassFixture<DocsApiFactory>
{
    [Fact]
    public async Task Create_stub_answers_the_bare_201_envelope()
    {
        AssertErrorEnvelope(
            await Api.PostJsonAsync(Client, "/v1/docs/create", new { }), 401, "COMMON.UNAUTHORIZED");

        var bearer = await BearerAsync();
        var response = await Api.PostJsonAsync(Client, "/v1/docs/create", new { }, bearer: bearer);
        AssertSuccessEnvelope(response, 201, "/v1/docs/create");
        // Frozen: the interceptor dropped the undefined payload — no data key.
        Assert.False(response.HasData, response.RawBody);
    }

    [Fact]
    public async Task Bulk_delete_stub_ignores_its_payload_and_answers_the_bare_200_envelope()
    {
        AssertErrorEnvelope(await DeleteAsync("/v1/docs"), 401, "COMMON.UNAUTHORIZED");

        var bearer = await BearerAsync();
        var (documentId, _, _) = await UploadOneAsync(bearer, Unique("keep") + ".txt", "keep"u8.ToArray());

        var response = await DeleteAsync("/v1/docs", bearer: bearer, body: new[] { documentId });
        AssertSuccessEnvelope(response, 200, "/v1/docs");
        Assert.False(response.HasData, response.RawBody);

        // Frozen: nothing was deleted — the controller never touched storage.
        var survivor = await Api.GetAsync(Client, $"/v1/docs/{documentId}", bearer: bearer);
        AssertSuccessEnvelope(survivor, 200, $"/v1/docs/{documentId}");

        // Any body shape is accepted (the frozen stub had no validation).
        var garbage = await DeleteAsync("/v1/docs", bearer: bearer, body: new { anything = true });
        AssertSuccessEnvelope(garbage, 200, "/v1/docs");
    }

    [Fact]
    public async Task No_document_table_column_can_hold_a_binary()
    {
        // DoD C4: binaries never go in the database — the blob store is the
        // only place a document body lives. Inspect the real schema.
        await Factory.EnsureSchemaAsync();
        var columns = await Factory.WithDbAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT table_name, column_name, data_type, udt_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name IN (
                    'documents', 'documents-version', 'document-storage-object',
                    'document-link', 'documents-generated', 'document-audit-log')
                """;
            var rows = new List<(string Table, string Column, string DataType, string UdtName)>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3)));
            }

            return rows;
        });

        // All six frozen tables exist...
        Assert.Equal(
            [
                "document-audit-log", "document-link", "document-storage-object",
                "documents", "documents-generated", "documents-version",
            ],
            columns.Select(c => c.Table).Distinct().Order().ToList());

        // ...and none of their columns has a binary-capable type.
        string[] binaryTypes = ["bytea", "oid", "bit", "bit varying"];
        var offending = columns
            .Where(c => binaryTypes.Contains(c.DataType) || binaryTypes.Contains(c.UdtName))
            .ToList();
        Assert.True(
            offending.Count == 0,
            $"binary-capable columns found: {string.Join(", ", offending.Select(c => $"{c.Table}.{c.Column} ({c.DataType})"))}");
    }

    [Fact]
    public async Task A_second_version_preserves_the_first()
    {
        // The frozen API creates version 1 per document; version max+1 is the
        // worker/store surface. Prove it at the store: the first version's
        // row, key, and blob survive the second's arrival, and the URL route
        // switches to the latest.
        var bearer = await BearerAsync();
        var (documentId, firstFileId, firstKey) = await UploadOneAsync(
            bearer, Unique("v1") + ".txt", "first version"u8.ToArray(), folderName: "versions");

        var secondFileId = Guid.NewGuid();
        var secondKey = $"versions/{secondFileId}";
        await Factory.WithScopeAsync(async services =>
        {
            var store = services.GetRequiredService<IDocumentStore>();
            var created = await store.AddGeneratedVersionAsync(new GeneratedVersionRecord(
                Guid.Parse(documentId),
                "v2.pdf",
                "application/pdf",
                3,
                secondFileId,
                secondKey,
                DocsApiFactory.Bucket,
                "manual-template",
                "{}",
                EntityId: null,
                EntityType: null));
            Assert.Equal(2, created);
        });

        var document = await Api.GetAsync(Client, $"/v1/docs/{documentId}", bearer: bearer);
        var versions = document.Data.GetProperty("versions");
        Assert.Equal(2, versions.GetArrayLength());
        Assert.Equal(1, versions[0].GetProperty("version").GetInt32());
        Assert.Equal(firstKey, versions[0].GetProperty("storage").GetProperty("path").GetString());
        Assert.Equal(
            firstFileId, versions[0].GetProperty("storage").GetProperty("id").GetString());
        Assert.Equal(2, versions[1].GetProperty("version").GetInt32());
        Assert.Equal(secondKey, versions[1].GetProperty("storage").GetProperty("path").GetString());

        // The first version's blob is untouched by the second's arrival.
        var presign = await Api.PostJsonAsync(Client, "/v1/docs/url", new
        {
            documents = new[]
            {
                new
                {
                    bucket = DocsApiFactory.Bucket,
                    files = new[] { new { folderName = "versions", fileInfo = new { fileId = firstFileId } } },
                },
            },
        }, bearer: bearer);
        using var http = new HttpClient();
        Assert.Equal(
            "first version",
            await http.GetStringAsync(
                presign.Data[0].GetProperty("files")[0].GetProperty("url").GetString()));

        // GET /{id}/url presigns the LATEST version (frozen sort by version DESC).
        var latestUrl = await Api.GetAsync(Client, $"/v1/docs/{documentId}/url", bearer: bearer);
        Assert.Contains(
            secondKey,
            latestUrl.Data.GetProperty("url").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_frozen_audit_table_exists_and_stays_empty()
    {
        // Pinned frozen behavior: document-audit-log is defined by the schema
        // but NO code path writes it (verified across the frozen tree at
        // freeze/typescript-2026-07-27). Uploads, versions, and deletions in
        // this suite must not have invented rows the frozen system never wrote.
        var count = await Factory.WithDbAsync(
            async db => await db.DocumentAuditLogs.CountAsync());
        Assert.Equal(0, count);
    }
}
