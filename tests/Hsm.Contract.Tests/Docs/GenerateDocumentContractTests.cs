using System.Text.RegularExpressions;
using Hsm.Contract.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Docs;

/// <summary>
/// POST /v1/docs/generate — behavior pinned from the frozen
/// docs.service.generateDocument + worker docs-processor.service.ts, with
/// QuestPDF replacing Puppeteer (plan U15): 201 { documentId, jobId }
/// immediately, then PENDING → PROCESSING → COMPLETED with version max+1,
/// storage object under "&lt;documentcode-lower&gt;/&lt;uuid&gt;", generation
/// provenance (template name + raw data), and FAILED on any render error.
/// </summary>
public sealed class GenerateDocumentContractTests(DocsApiFactory factory)
    : DocsContractTest(factory), IClassFixture<DocsApiFactory>
{
    [Fact]
    public async Task Generation_requires_authentication()
    {
        var response = await Api.PostJsonAsync(Client, "/v1/docs/generate", new { });
        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Body_validation_pins_the_frozen_constraints()
    {
        var bearer = await BearerAsync();
        var response = await Api.PostJsonAsync(Client, "/v1/docs/generate", new { }, bearer: bearer);
        AssertValidationFailure(response, "templateIdentifier", "isNotEmpty");
        AssertValidationFailure(response, "data", "isObject");
        AssertValidationFailure(response, "title", "isNotEmpty");

        var unknown = await Api.PostJsonAsync(Client, "/v1/docs/generate", new
        {
            templateIdentifier = "x",
            data = new { },
            title = "t",
            extra = 1,
        }, bearer: bearer);
        AssertValidationFailure(unknown, "extra", "whitelistValidation");
    }

    [Fact]
    public async Task Generates_a_pdf_and_persists_the_frozen_graph()
    {
        var bearer = await BearerAsync();
        var template = await Factory.SeedDocsTemplateAsync(
            Unique("gen"), schemaJson: """{"patientName":"string"}""");

        var response = await Api.PostJsonAsync(Client, "/v1/docs/generate", new
        {
            templateIdentifier = template,
            data = new { patientName = "Ada Lovelace" },
            title = "Documento generado",
            description = "desde plantilla",
        }, bearer: bearer);
        AssertSuccessEnvelope(response, 201, "/v1/docs/generate");
        var documentId = response.Data.GetProperty("documentId").GetString()!;
        Assert.True(Guid.TryParse(documentId, out _));
        Assert.False(string.IsNullOrEmpty(response.Data.GetProperty("jobId").GetString()));

        var document = await WaitForTerminalStatusAsync(bearer, documentId);
        Assert.Equal("COMPLETED", document.GetProperty("status").GetString());
        Assert.Equal("GENERATED", document.GetProperty("type").GetString());
        Assert.Equal("TEMPLATE", document.GetProperty("source").GetString());
        Assert.Equal("Documento generado", document.GetProperty("title").GetString());
        Assert.Equal("desde plantilla", document.GetProperty("description").GetString());

        var versions = document.GetProperty("versions");
        Assert.Equal(1, versions.GetArrayLength());
        var version = versions[0];
        Assert.Equal(1, version.GetProperty("version").GetInt32());
        Assert.Matches(new Regex(@"^HCU-001-\d+\.pdf$"), version.GetProperty("filename").GetString()!);
        Assert.Equal("application/pdf", version.GetProperty("mimeType").GetString());
        Assert.True(version.GetProperty("size").GetInt64() > 0);

        var storage = version.GetProperty("storage");
        Assert.Equal(DocsApiFactory.Bucket, storage.GetProperty("bucket").GetString());
        var path = storage.GetProperty("path").GetString()!;
        Assert.StartsWith("hcu-001/", path, StringComparison.Ordinal);
        Assert.EndsWith(storage.GetProperty("id").GetString()!, path, StringComparison.Ordinal);

        // Generation provenance (frozen documents-generated row): the
        // template's name plus the raw substitution data, jsonb.
        var versionId = Guid.Parse(version.GetProperty("id").GetString()!);
        var generated = await Factory.WithDbAsync(async db =>
            await db.DocumentGeneratedRecords.SingleAsync(g => g.VersionId == versionId));
        Assert.Equal(template, generated.TemplateName);
        Assert.Contains("Ada Lovelace", generated.DataJson, StringComparison.Ordinal);

        // The generated object is a real PDF, reachable via presigned URL.
        var urlResponse = await Api.GetAsync(Client, $"/v1/docs/{documentId}/url", bearer: bearer);
        AssertSuccessEnvelope(urlResponse, 200, $"/v1/docs/{documentId}/url");
        using var http = new HttpClient();
        var pdf = await http.GetByteArrayAsync(urlResponse.Data.GetProperty("url").GetString());
        Assert.True(pdf.Length > 4);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Fact]
    public async Task Generation_with_entity_writes_link_and_document_columns()
    {
        var bearer = await BearerAsync();
        var template = await Factory.SeedDocsTemplateAsync(Unique("ent"));
        var entityId = Unique("case");

        var response = await Api.PostJsonAsync(Client, "/v1/docs/generate", new
        {
            templateIdentifier = template,
            data = new { patientName = "Grace" },
            title = "Con entidad",
            entityId,
            entityType = "case",
        }, bearer: bearer);
        var documentId = response.Data.GetProperty("documentId").GetString()!;
        var document = await WaitForTerminalStatusAsync(bearer, documentId);
        Assert.Equal("COMPLETED", document.GetProperty("status").GetString());
        Assert.Equal(entityId, document.GetProperty("entityId").GetString());
        Assert.Equal("case", document.GetProperty("entityType").GetString());

        var links = await Factory.WithDbAsync(async db =>
            await db.DocumentLinks.Where(l => l.DocumentId == Guid.Parse(documentId)).ToListAsync());
        var link = Assert.Single(links);
        Assert.Equal(entityId, link.EntityId);

        // Unlike uploads, generation DOES set the document's entity columns,
        // so the list's pair filter finds it (frozen asymmetry, pinned).
        var listed = await Api.GetAsync(
            Client, $"/v1/docs?entityId={entityId}&entityType=case", bearer: bearer);
        Assert.Single(listed.Data.EnumerateArray());
    }

    [Fact]
    public async Task Unknown_template_marks_the_document_failed()
    {
        var bearer = await BearerAsync();
        var response = await Api.PostJsonAsync(Client, "/v1/docs/generate", new
        {
            templateIdentifier = Unique("missing"),
            data = new { },
            title = "Sin plantilla",
        }, bearer: bearer);
        AssertSuccessEnvelope(response, 201, "/v1/docs/generate");

        var document = await WaitForTerminalStatusAsync(
            bearer, response.Data.GetProperty("documentId").GetString()!);
        Assert.Equal("FAILED", document.GetProperty("status").GetString());
        Assert.Empty(document.GetProperty("versions").EnumerateArray());
    }

    [Fact]
    public async Task Inactive_template_marks_the_document_failed()
    {
        var bearer = await BearerAsync();
        var template = await Factory.SeedDocsTemplateAsync(Unique("off"), isActive: false);
        var response = await Api.PostJsonAsync(Client, "/v1/docs/generate", new
        {
            templateIdentifier = template,
            data = new { },
            title = "Inactiva",
        }, bearer: bearer);
        var document = await WaitForTerminalStatusAsync(
            bearer, response.Data.GetProperty("documentId").GetString()!);
        Assert.Equal("FAILED", document.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Schema_mismatch_marks_the_document_failed_and_logs_the_parse()
    {
        var bearer = await BearerAsync();
        var template = await Factory.SeedDocsTemplateAsync(
            Unique("schema"), schemaJson: """{"patientName":"string"}""");
        var response = await Api.PostJsonAsync(Client, "/v1/docs/generate", new
        {
            templateIdentifier = template,
            data = new { },
            title = "Datos incompletos",
        }, bearer: bearer);
        var document = await WaitForTerminalStatusAsync(
            bearer, response.Data.GetProperty("documentId").GetString()!);
        Assert.Equal("FAILED", document.GetProperty("status").GetString());

        // The frozen worker's parse wrote a template_parse_logs failure row.
        var logged = await Factory.WithDbAsync(async db =>
            await db.TemplateParseLogs.AnyAsync(l => l.TemplateName == template && !l.Success));
        Assert.True(logged);
    }

    [Fact]
    public async Task Non_pdf_format_marks_the_document_failed()
    {
        // Deliberate scope boundary (documented in the U15 report): the
        // frozen EXCEL path (exceljs) is not carried into the minor; EXCEL
        // now fails the job exactly as the frozen WORD path always did.
        var bearer = await BearerAsync();
        var template = await Factory.SeedDocsTemplateAsync(Unique("xls"), format: "EXCEL");
        var response = await Api.PostJsonAsync(Client, "/v1/docs/generate", new
        {
            templateIdentifier = template,
            data = new { },
            title = "Excel",
        }, bearer: bearer);
        var document = await WaitForTerminalStatusAsync(
            bearer, response.Data.GetProperty("documentId").GetString()!);
        Assert.Equal("FAILED", document.GetProperty("status").GetString());
    }
}
