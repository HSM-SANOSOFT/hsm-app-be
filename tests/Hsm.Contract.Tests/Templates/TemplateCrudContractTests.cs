using System.Text.Json;
using Hsm.Contract.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Templates;

/// <summary>
/// GET/POST /v1/templates, GET /{identifier}, PUT/DELETE /{id} — behavior
/// pinned from the frozen templates.controller/templates.service at
/// freeze/typescript-2026-07-27: the three shapes with distinct field sets,
/// id-or-name lookup, name uniqueness (409), category immutability, and the
/// in-use deletion invariant surfacing as the DOMAIN 409.
/// </summary>
public sealed class TemplateCrudContractTests(TemplatesApiFactory factory)
    : TemplatesContractTest(factory), IClassFixture<TemplatesApiFactory>
{
    [Fact]
    public async Task Templates_require_authentication()
    {
        var response = await Api.GetAsync(Client, "/v1/templates");
        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Create_base_template_returns_detail_with_null_metadata()
    {
        var bearer = await BearerAsync();
        var name = Unique("base");

        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "BASE",
            name,
            description = "layout",
            schema = new { },
            content = "<html>{{{body}}}</html>",
        }, bearer: bearer);

        AssertSuccessEnvelope(response, 201, "/v1/templates");
        var template = response.Data.GetProperty("template");
        Assert.Equal("BASE", template.GetProperty("category").GetString());
        Assert.Equal(name, template.GetProperty("name").GetString());
        Assert.True(template.GetProperty("isActive").GetBoolean());
        Assert.Equal("layout", template.GetProperty("description").GetString());
        Assert.Equal("<html>{{{body}}}</html>", template.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, template.GetProperty("metadata").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.Data.GetProperty("baseTemplate").ValueKind);
    }

    [Fact]
    public async Task Create_email_template_carries_email_shape_and_base()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);
        var name = Unique("email");

        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "EMAIL_INTERNAL",
            name,
            schema = new { patientName = "string" },
            content = "<p>Hola {{patientName}}</p>",
            baseTemplateId = baseId,
            email = new
            {
                subject = "Bienvenido",
                fromEmail = "no-reply@hsm.test",
                fromName = "HSM",
                cc = new[] { "archive@hsm.test" },
                hasAttachment = true,
            },
        }, bearer: bearer);

        AssertSuccessEnvelope(response, 201, "/v1/templates");
        var template = response.Data.GetProperty("template");
        var metadata = template.GetProperty("metadata");
        Assert.Equal("Bienvenido", metadata.GetProperty("subject").GetString());
        Assert.Equal("no-reply@hsm.test", metadata.GetProperty("fromEmail").GetString());
        Assert.Equal("HSM", metadata.GetProperty("fromName").GetString());
        Assert.Equal("archive@hsm.test", metadata.GetProperty("cc")[0].GetString());
        Assert.False(metadata.TryGetProperty("bcc", out _));
        Assert.True(metadata.GetProperty("hasAttachment").GetBoolean());
        Assert.Equal(baseId, response.Data.GetProperty("baseTemplate").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Create_sms_and_doc_templates_carry_their_distinct_shapes()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);

        var sms = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "SMS_EXTERNAL",
            name = Unique("sms"),
            schema = new { pin = "string" },
            content = "PIN: {{pin}}",
            baseTemplateId = baseId,
            sms = new { provider = "twilio", templateName = "appt_reminder", from = "+15005550006" },
        }, bearer: bearer);
        AssertSuccessEnvelope(sms, 201, "/v1/templates");
        var smsMeta = sms.Data.GetProperty("template").GetProperty("metadata");
        Assert.Equal("twilio", smsMeta.GetProperty("provider").GetString());
        Assert.Equal("appt_reminder", smsMeta.GetProperty("templateName").GetString());
        Assert.Equal("+15005550006", smsMeta.GetProperty("from").GetString());

        var doc = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "DOCS",
            name = Unique("doc"),
            schema = new { },
            content = "<p>Doc</p>",
            baseTemplateId = baseId,
            doc = new { documentCode = "HCU-001", format = "PDF", size = "A4", orientation = "PORTRAIT" },
        }, bearer: bearer);
        AssertSuccessEnvelope(doc, 201, "/v1/templates");
        var docMeta = doc.Data.GetProperty("template").GetProperty("metadata");
        Assert.Equal("HCU-001", docMeta.GetProperty("documentCode").GetString());
        Assert.Equal("PDF", docMeta.GetProperty("format").GetString());
        Assert.Equal("A4", docMeta.GetProperty("size").GetString());
        Assert.Equal("PORTRAIT", docMeta.GetProperty("orientation").GetString());
    }

    [Fact]
    public async Task Create_rejects_duplicate_name_with_conflict()
    {
        var bearer = await BearerAsync();
        var name = Unique("dup");
        await CreateBaseAsync(bearer, name);

        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "BASE",
            name,
            schema = new { },
            content = "x",
        }, bearer: bearer);

        var issue = AssertErrorEnvelope(response, 409, "COMMON.CONFLICT");
        Assert.Equal($"Template with name '{name}' already exists", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Create_base_with_base_reference_is_the_frozen_shape_error()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);

        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "BASE",
            name = Unique("base"),
            schema = new { },
            content = "x",
            baseTemplateId = baseId,
        }, bearer: bearer);

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal(
            "Invalid template payload: BASE templates must not have baseTemplateId",
            issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Create_email_without_email_block_fails_validation()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);

        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "EMAIL_INTERNAL",
            name = Unique("email"),
            schema = new { },
            content = "x",
            baseTemplateId = baseId,
        }, bearer: bearer);

        AssertValidationFailure(response, "email", "isNotEmpty");
    }

    [Fact]
    public async Task Create_rejects_malformed_schema_and_bad_handlebars()
    {
        var bearer = await BearerAsync();

        var malformed = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "BASE",
            name = Unique("bad"),
            schema = new { patientName = "unknown-tag" },
            content = "ok",
        }, bearer: bearer);
        var issue = AssertErrorEnvelope(malformed, 400, "COMMON.VALIDATION");
        Assert.StartsWith(
            "Invalid template payload: schema is malformed",
            issue.GetProperty("message").GetString(),
            StringComparison.Ordinal);

        var badHbs = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "BASE",
            name = Unique("bad"),
            schema = new { },
            content = "{{#if x}}unclosed",
        }, bearer: bearer);
        var hbsIssue = AssertErrorEnvelope(badHbs, 400, "COMMON.VALIDATION");
        Assert.StartsWith(
            "Invalid Handlebars template:",
            hbsIssue.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_rejects_non_base_reference()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);
        var emailId = await CreateEmailTemplateAsync(bearer, baseId);

        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "EMAIL_INTERNAL",
            name = Unique("email"),
            schema = new { },
            content = "x",
            baseTemplateId = emailId,
            email = new { subject = "s", fromEmail = "a@b.c", fromName = "n" },
        }, bearer: bearer);

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal(
            "Invalid template payload: baseTemplateId must reference a template with category=BASE",
            issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Create_rejects_unknown_properties()
    {
        var bearer = await BearerAsync();
        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "BASE",
            name = Unique("base"),
            schema = new { },
            content = "x",
            hacker = true,
        }, bearer: bearer);
        AssertValidationFailure(response, "hacker", "whitelistValidation");
    }

    [Fact]
    public async Task List_returns_templates_with_children_and_category_filter()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);
        var emailId = await CreateEmailTemplateAsync(bearer, baseId);

        var all = await Api.GetAsync(Client, "/v1/templates", bearer: bearer);
        AssertSuccessEnvelope(all, 200, "/v1/templates");
        var items = all.Data.EnumerateArray().ToList();
        Assert.Contains(items, t => t.GetProperty("id").GetString() == emailId);
        AssertSyntheticPagination(all, items.Count);
        // name ASC ordering.
        var names = items.Select(t => t.GetProperty("name").GetString()!).ToList();
        Assert.Equal([.. names.OrderBy(n => n, StringComparer.Ordinal)], names);

        var filtered = await Api.GetAsync(Client, "/v1/templates?category=EMAIL_INTERNAL", bearer: bearer);
        AssertSuccessEnvelope(filtered, 200, "/v1/templates?category=EMAIL_INTERNAL");
        Assert.All(
            filtered.Data.EnumerateArray(),
            t => Assert.Equal("EMAIL_INTERNAL", t.GetProperty("category").GetString()));
        var match = filtered.Data.EnumerateArray().First(t => t.GetProperty("id").GetString() == emailId);
        Assert.Equal(
            "no-reply@hsm.test", match.GetProperty("metadata").GetProperty("fromEmail").GetString());

        var invalid = await Api.GetAsync(Client, "/v1/templates?category=NOPE", bearer: bearer);
        AssertValidationFailure(invalid, "category", "isEnum");
    }

    [Fact]
    public async Task Get_resolves_by_id_and_by_name_and_404s_unknown()
    {
        var bearer = await BearerAsync();
        var name = Unique("base");
        var id = await CreateBaseAsync(bearer, name);

        var byId = await Api.GetAsync(Client, $"/v1/templates/{id}", bearer: bearer);
        AssertSuccessEnvelope(byId, 200, $"/v1/templates/{id}");
        Assert.Equal(name, byId.Data.GetProperty("template").GetProperty("name").GetString());

        var byName = await Api.GetAsync(Client, $"/v1/templates/{name}", bearer: bearer);
        AssertSuccessEnvelope(byName, 200, $"/v1/templates/{name}");
        Assert.Equal(id, byName.Data.GetProperty("template").GetProperty("id").GetString());

        var missing = await Api.GetAsync(Client, "/v1/templates/no_such_template", bearer: bearer);
        var issue = AssertErrorEnvelope(missing, 404, "COMMON.NOT_FOUND");
        Assert.Equal("Template 'no_such_template' not found", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Update_patches_fields_and_child_block_and_guards_category()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);
        var emailId = await CreateEmailTemplateAsync(bearer, baseId);
        var newName = Unique("renamed");

        var updated = await Api.PutJsonAsync(Client, $"/v1/templates/{emailId}", new
        {
            name = newName,
            content = "<p>Estimado {{patientName}}</p>",
            email = new { subject = "Nuevo asunto", fromEmail = "care@hsm.test", fromName = "Care" },
        }, bearer: bearer);

        AssertSuccessEnvelope(updated, 200, $"/v1/templates/{emailId}");
        var template = updated.Data.GetProperty("template");
        Assert.Equal(newName, template.GetProperty("name").GetString());
        Assert.Equal("<p>Estimado {{patientName}}</p>", template.GetProperty("content").GetString());
        Assert.Equal("Nuevo asunto", template.GetProperty("metadata").GetProperty("subject").GetString());

        var categoryChange = await Api.PutJsonAsync(
            Client, $"/v1/templates/{emailId}", new { category = "SMS_INTERNAL" }, bearer: bearer);
        var issue = AssertErrorEnvelope(categoryChange, 400, "COMMON.VALIDATION");
        Assert.Equal(
            "Invalid template payload: category is immutable; create a new template instead",
            issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Update_rejects_duplicate_name_and_malformed_id()
    {
        var bearer = await BearerAsync();
        var takenName = Unique("taken");
        await CreateBaseAsync(bearer, takenName);
        var otherId = await CreateBaseAsync(bearer);

        var clash = await Api.PutJsonAsync(
            Client, $"/v1/templates/{otherId}", new { name = takenName }, bearer: bearer);
        AssertErrorEnvelope(clash, 409, "COMMON.CONFLICT");

        var malformed = await Api.PutJsonAsync(
            Client, "/v1/templates/not-a-uuid", new { name = "x" }, bearer: bearer);
        var issue = AssertErrorEnvelope(malformed, 400, "COMMON.VALIDATION");
        Assert.Equal("Validation failed (uuid is expected)", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Delete_removes_parent_and_child_rows()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);
        var emailId = await CreateEmailTemplateAsync(bearer, baseId);

        var deleted = await DeleteAsync($"/v1/templates/{emailId}", bearer);
        AssertSuccessEnvelope(deleted, 200, $"/v1/templates/{emailId}");
        Assert.Equal(emailId, deleted.Data.GetProperty("id").GetString());

        var gone = await Factory.WithDbAsync(async db =>
        {
            var parent = await db.Templates.AnyAsync(t => t.Id == Guid.Parse(emailId));
            var child = await db.Set<Hsm.Domain.Templates.TemplateEmail>()
                .AnyAsync(e => e.Id == Guid.Parse(emailId));
            return (parent, child);
        });
        Assert.False(gone.parent);
        Assert.False(gone.child);

        var missing = await DeleteAsync($"/v1/templates/{emailId}", bearer);
        AssertErrorEnvelope(missing, 404, "COMMON.NOT_FOUND");
    }

    [Fact]
    public async Task Delete_of_an_in_use_base_is_the_domain_conflict_not_a_database_error()
    {
        var bearer = await BearerAsync();
        var baseId = await CreateBaseAsync(bearer);
        await CreateEmailTemplateAsync(bearer, baseId);

        var response = await DeleteAsync($"/v1/templates/{baseId}", bearer);

        var issue = AssertErrorEnvelope(response, 409, "COMMON.CONFLICT");
        Assert.Equal(
            $"Template '{baseId}' is referenced as a base by other templates and cannot be deleted",
            issue.GetProperty("message").GetString());
        // The row is untouched.
        var stillThere = await Factory.WithDbAsync(
            db => db.Templates.AnyAsync(t => t.Id == Guid.Parse(baseId)));
        Assert.True(stillThere);
    }

    private async Task<ApiResponse> DeleteAsync(string path, string bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        return await ApiResponse.FromAsync(await Client.SendAsync(request));
    }
}
