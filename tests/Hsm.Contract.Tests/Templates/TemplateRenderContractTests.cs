using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Templates;

/// <summary>
/// POST /v1/templates/validate and /v1/templates/draft-render — behavior
/// pinned from the frozen templates.service: validate always answers a
/// SUCCESS envelope carrying valid/issues (missing required fields fail with
/// path/expected/received), and draft-render composes unsaved Handlebars with
/// {{body}} base inheritance. Neither writes a parse log.
/// </summary>
public sealed class TemplateRenderContractTests(TemplatesApiFactory factory)
    : TemplatesContractTest(factory), IClassFixture<TemplatesApiFactory>
{
    [Fact]
    public async Task Validate_passes_matching_data()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);
        var name = Unique("email");
        var id = await CreateEmailTemplateAsync(
            bearer, baseId, name, schema: new { patientName = "string", age = "number?" });

        var response = await Api.PostJsonAsync(Client, "/v1/templates/validate", new
        {
            identifier = name,
            data = new { patientName = "Ada" },
        }, bearer: bearer);

        AssertSuccessEnvelope(response, 201, "/v1/templates/validate");
        Assert.True(response.Data.GetProperty("valid").GetBoolean());
        Assert.Equal(id, response.Data.GetProperty("templateId").GetString());
        Assert.False(response.Data.TryGetProperty("issues", out _));
    }

    [Fact]
    public async Task Validate_reports_missing_required_field_as_issue_not_error_status()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);
        var name = Unique("email");
        await CreateEmailTemplateAsync(
            bearer, baseId, name, schema: new { patientName = "string", age = "number" });

        var response = await Api.PostJsonAsync(Client, "/v1/templates/validate", new
        {
            identifier = name,
            data = new { patientName = 42 },
        }, bearer: bearer);

        AssertSuccessEnvelope(response, 201, "/v1/templates/validate");
        Assert.False(response.Data.GetProperty("valid").GetBoolean());
        var issues = response.Data.GetProperty("issues").EnumerateArray().ToList();
        var wrongType = issues.First(i => i.GetProperty("path").GetString() == "patientName");
        Assert.Equal("string", wrongType.GetProperty("expected").GetString());
        Assert.Equal("number", wrongType.GetProperty("received").GetString());
        var missing = issues.First(i => i.GetProperty("path").GetString() == "age");
        Assert.Equal("number", missing.GetProperty("expected").GetString());
        Assert.Equal("undefined", missing.GetProperty("received").GetString());
    }

    [Fact]
    public async Task Validate_unknown_identifier_is_404_and_writes_nothing()
    {
        var bearer = await BearerAsync();

        var response = await Api.PostJsonAsync(Client, "/v1/templates/validate", new
        {
            identifier = "ghost_template",
            data = new { },
        }, bearer: bearer);

        var issue = AssertErrorEnvelope(response, 404, "COMMON.NOT_FOUND");
        Assert.Equal("Template 'ghost_template' not found", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Validate_never_writes_a_parse_log()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);
        var name = Unique("email");
        await CreateEmailTemplateAsync(bearer, baseId, name);

        var before = await Factory.WithDbAsync(db => db.TemplateParseLogs.CountAsync());
        await Api.PostJsonAsync(Client, "/v1/templates/validate", new
        {
            identifier = name,
            data = new { patientName = "Ada" },
        }, bearer: bearer);
        var after = await Factory.WithDbAsync(db => db.TemplateParseLogs.CountAsync());

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Draft_render_composes_content_with_sample_data()
    {
        var bearer = await BearerAsync();

        var response = await Api.PostJsonAsync(Client, "/v1/templates/draft-render", new
        {
            content = "<p>Hola {{patientName}}</p>",
            sampleData = new { patientName = "Ada" },
        }, bearer: bearer);

        AssertSuccessEnvelope(response, 201, "/v1/templates/draft-render");
        Assert.Equal("<p>Hola Ada</p>", response.Data.GetProperty("html").GetString());
    }

    [Fact]
    public async Task Draft_render_wraps_content_in_the_base_template_via_body()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);

        var response = await Api.PostJsonAsync(Client, "/v1/templates/draft-render", new
        {
            content = "<p>Hola {{patientName}}</p>",
            baseTemplateId = baseId,
            sampleData = new { patientName = "Ada" },
        }, bearer: bearer);

        AssertSuccessEnvelope(response, 201, "/v1/templates/draft-render");
        Assert.Equal(
            "<html><body><p>Hola Ada</p></body></html>",
            response.Data.GetProperty("html").GetString());
    }

    [Fact]
    public async Task Draft_render_rejects_bad_base_and_bad_handlebars()
    {
        var bearer = await BearerAsync();
        var ghost = Guid.NewGuid();

        var missingBase = await Api.PostJsonAsync(Client, "/v1/templates/draft-render", new
        {
            content = "x",
            baseTemplateId = ghost.ToString(),
        }, bearer: bearer);
        var missingIssue = AssertErrorEnvelope(missingBase, 404, "COMMON.NOT_FOUND");
        Assert.Equal($"Template '{ghost}' not found", missingIssue.GetProperty("message").GetString());

        var badHbs = await Api.PostJsonAsync(Client, "/v1/templates/draft-render", new
        {
            content = "{{#each x}}unclosed",
        }, bearer: bearer);
        var hbsIssue = AssertErrorEnvelope(badHbs, 400, "COMMON.VALIDATION");
        Assert.StartsWith(
            "Invalid Handlebars template:",
            hbsIssue.GetProperty("message").GetString(),
            StringComparison.Ordinal);

        var nonBase = await Api.PostJsonAsync(Client, "/v1/templates/draft-render", new
        {
            content = "x",
            baseTemplateId = await NonBaseTemplateIdAsync(bearer),
        }, bearer: bearer);
        var nonBaseIssue = AssertErrorEnvelope(nonBase, 400, "COMMON.VALIDATION");
        Assert.Equal(
            "Invalid template payload: baseTemplateId must reference a template with category=BASE",
            nonBaseIssue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Draft_render_requires_content()
    {
        var bearer = await BearerAsync();
        var response = await Api.PostJsonAsync(
            Client, "/v1/templates/draft-render", new { }, bearer: bearer);
        AssertValidationFailure(response, "content", "isNotEmpty");
    }

    [Fact]
    public async Task Validate_reports_uncompilable_stored_content()
    {
        var bearer = await BearerAsync();
        var name = Unique("broken");
        // Seed a template whose stored content no longer compiles (possible in
        // the frozen system through legacy rows).
        await Factory.WithDbAsync(async db =>
        {
            db.Templates.Add(new Hsm.Domain.Templates.Template
            {
                Id = Guid.NewGuid(),
                Category = "BASE",
                Name = name,
                IsActive = true,
                SchemaJson = "{}",
                Content = "{{#if x}}unclosed",
            });
            await db.SaveChangesAsync();
            return true;
        });

        var response = await Api.PostJsonAsync(Client, "/v1/templates/validate", new
        {
            identifier = name,
            data = new { },
        }, bearer: bearer);

        AssertSuccessEnvelope(response, 201, "/v1/templates/validate");
        Assert.False(response.Data.GetProperty("valid").GetBoolean());
        var issue = response.Data.GetProperty("issues")[0];
        Assert.Equal("content", issue.GetProperty("path").GetString());
        Assert.Equal("compilable Handlebars", issue.GetProperty("expected").GetString());
        Assert.Equal(JsonValueKind.String, issue.GetProperty("received").ValueKind);
    }

    private async Task<string> NonBaseTemplateIdAsync(string bearer)
    {
        var baseId = await CreateBaseAsync(bearer);
        return await CreateEmailTemplateAsync(bearer, baseId);
    }
}
