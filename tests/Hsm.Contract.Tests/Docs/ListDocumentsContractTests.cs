
namespace Hsm.Contract.Tests.Docs;

/// <summary>
/// GET /v1/docs — behavior pinned from the frozen docs.controller/
/// docs.service at freeze/typescript-2026-07-27: createdBy scoping, deleted
/// rows excluded, createdAt DESC, page/limit with the frozen
/// buildPaginationMeta block, and the ListDocumentsQueryDto validation
/// surface (type/status enums, page min 1, limit 1..100, whitelist).
/// </summary>
public sealed class ListDocumentsContractTests(DocsApiFactory factory)
    : DocsContractTest(factory), IClassFixture<DocsApiFactory>
{
    [Fact]
    public async Task Listing_requires_authentication()
    {
        var response = await Api.GetAsync(Client, "/v1/docs");
        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Empty_list_answers_the_frozen_pagination_block()
    {
        var bearer = await BearerAsync();
        var response = await Api.GetAsync(Client, "/v1/docs", bearer: bearer);
        AssertSuccessEnvelope(response, 200, "/v1/docs");
        Assert.Empty(response.Data.EnumerateArray());
        AssertPagination(response, page: 1, pageSize: 20, totalItems: 0, totalPages: 0);
    }

    [Fact]
    public async Task Lists_own_documents_newest_first_and_scopes_by_owner()
    {
        var owner = await BearerAsync();
        var other = await BearerAsync();
        var (firstId, _, _) = await UploadOneAsync(owner, Unique("a") + ".txt", "first"u8.ToArray());
        await Task.Delay(20);
        var (secondId, _, _) = await UploadOneAsync(owner, Unique("b") + ".txt", "second"u8.ToArray());

        var response = await Api.GetAsync(Client, "/v1/docs", bearer: owner);
        AssertSuccessEnvelope(response, 200, "/v1/docs");
        var ids = response.Data.EnumerateArray().Select(d => d.GetProperty("id").GetString()).ToList();
        Assert.Equal([secondId, firstId], ids);

        var item = response.Data[0];
        Assert.Equal("UPLOADED", item.GetProperty("type").GetString());
        Assert.Equal("COMPLETED", item.GetProperty("status").GetString());
        Assert.Equal("MANUAL", item.GetProperty("source").GetString());
        // List items carry no versions (the frozen list loads no relations).
        Assert.False(item.TryGetProperty("versions", out _));

        var unrelated = await Api.GetAsync(Client, "/v1/docs", bearer: other);
        Assert.Empty(unrelated.Data.EnumerateArray());
    }

    [Fact]
    public async Task Type_and_status_filters_apply()
    {
        var bearer = await BearerAsync();
        await UploadOneAsync(bearer, Unique("f") + ".txt", "filtered"u8.ToArray());

        var uploaded = await Api.GetAsync(Client, "/v1/docs?type=UPLOADED", bearer: bearer);
        Assert.Single(uploaded.Data.EnumerateArray());

        var generated = await Api.GetAsync(Client, "/v1/docs?type=GENERATED", bearer: bearer);
        Assert.Empty(generated.Data.EnumerateArray());

        var completed = await Api.GetAsync(Client, "/v1/docs?status=COMPLETED", bearer: bearer);
        Assert.Single(completed.Data.EnumerateArray());

        var failed = await Api.GetAsync(Client, "/v1/docs?status=FAILED", bearer: bearer);
        Assert.Empty(failed.Data.EnumerateArray());
    }

    [Fact]
    public async Task Entity_filter_requires_both_parts_and_reads_document_columns()
    {
        var bearer = await BearerAsync();
        var entityId = Unique("patient");
        await UploadOneAsync(
            bearer, Unique("linked") + ".txt", "linked"u8.ToArray(),
            entityId: entityId, entityType: "patient");

        // Frozen quirk pinned: uploads create a LINK row but never write the
        // document's own entity columns, and the list filter reads the
        // columns — so the pair filter does NOT return the uploaded doc.
        var byPair = await Api.GetAsync(
            Client, $"/v1/docs?entityId={entityId}&entityType=patient", bearer: bearer);
        Assert.Empty(byPair.Data.EnumerateArray());

        // Frozen: entityId alone is ignored (the filter needs both parts).
        var byIdOnly = await Api.GetAsync(Client, $"/v1/docs?entityId={entityId}", bearer: bearer);
        Assert.Single(byIdOnly.Data.EnumerateArray());
    }

    [Fact]
    public async Task Paging_computes_the_frozen_totals()
    {
        var bearer = await BearerAsync();
        for (var i = 0; i < 3; i++)
        {
            await UploadOneAsync(bearer, Unique($"p{i}") + ".txt", "page"u8.ToArray());
        }

        var firstPage = await Api.GetAsync(Client, "/v1/docs?page=1&limit=2", bearer: bearer);
        AssertSuccessEnvelope(firstPage, 200, "/v1/docs?page=1&limit=2");
        Assert.Equal(2, firstPage.Data.GetArrayLength());
        AssertPagination(firstPage, page: 1, pageSize: 2, totalItems: 3, totalPages: 2);

        var secondPage = await Api.GetAsync(Client, "/v1/docs?page=2&limit=2", bearer: bearer);
        Assert.Equal(1, secondPage.Data.GetArrayLength());
    }

    [Fact]
    public async Task Query_validation_pins_the_frozen_constraints()
    {
        var bearer = await BearerAsync();

        AssertValidationFailure(
            await Api.GetAsync(Client, "/v1/docs?limit=101", bearer: bearer), "limit", "max");
        AssertValidationFailure(
            await Api.GetAsync(Client, "/v1/docs?page=0", bearer: bearer), "page", "min");
        AssertValidationFailure(
            await Api.GetAsync(Client, "/v1/docs?page=abc", bearer: bearer), "page", "isInt");
        AssertValidationFailure(
            await Api.GetAsync(Client, "/v1/docs?type=BOGUS", bearer: bearer), "type", "isEnum");
        AssertValidationFailure(
            await Api.GetAsync(Client, "/v1/docs?status=BOGUS", bearer: bearer), "status", "isEnum");
        AssertValidationFailure(
            await Api.GetAsync(Client, "/v1/docs?nope=1", bearer: bearer), "nope", "whitelistValidation");
    }
}
