using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Domain.Identity;
using Hsm.Domain.Templates;

namespace Hsm.Api.Tests.Templates;

public class TemplatesEndpointTests(TemplatesFactory factory) : IClassFixture<TemplatesFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static object BaseBody(string name, string content = "<html>{{{body}}}</html>") => new
    {
        category = TemplateCategories.Base,
        name,
        description = (string?)null,
        isActive = true,
        schema = new { },
        content,
    };

    private static object EmailBody(string name, Guid baseTemplateId) => new
    {
        category = TemplateCategories.EmailExternal,
        name,
        description = "an email template",
        isActive = true,
        schema = new { name = "string" },
        content = "Hello {{name}}",
        baseTemplateId = baseTemplateId.ToString(),
        email = new
        {
            subject = "Welcome",
            fromEmail = "noreply@api.test",
            fromName = "HSM",
            cc = new[] { "cc@api.test" },
            hasAttachment = true,
        },
    };

    private static object DocBody(string name, Guid baseTemplateId) => new
    {
        category = TemplateCategories.Docs,
        name,
        isActive = true,
        schema = new { },
        content = "Doc body",
        baseTemplateId = baseTemplateId.ToString(),
        doc = new
        {
            documentCode = DocumentCodes.All[0],
            format = DocumentFormats.All[0],
            size = DocumentSizes.All[0],
            orientation = DocumentOrientations.All[0],
        },
    };

    private static object SmsBody(string name, Guid baseTemplateId) => new
    {
        category = TemplateCategories.SmsExternal,
        name,
        isActive = true,
        schema = new { },
        content = "Sms body",
        baseTemplateId = baseTemplateId.ToString(),
        sms = new { provider = "twilio", templateName = "otp", from = "+10000000000" },
    };

    private static async Task<(Guid Id, JsonElement Body)> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/templates", body, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal($"/api/v1/templates/{json.GetProperty("id").GetGuid()}", response.Headers.Location?.ToString());
        return (json.GetProperty("id").GetGuid(), json);
    }

    private static async Task<Guid> CreateBaseAsync(HttpClient client, string? name = null)
    {
        var (id, _) = await CreateAsync(client, BaseBody(name ?? Unique("base")));
        return id;
    }

    [Fact]
    public async Task Creating_a_base_template_returns_201_with_no_metadata_block()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var name = Unique("base");

        var (id, body) = await CreateAsync(client, BaseBody(name));

        Assert.Equal(id, body.GetProperty("id").GetGuid());
        // Identifier and Name currently alias the same domain column (Template
        // has no separate slug) — both must carry the authored name.
        Assert.Equal(name, body.GetProperty("identifier").GetString());
        Assert.Equal(name, body.GetProperty("name").GetString());
        Assert.Equal(TemplateCategories.Base, body.GetProperty("category").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal("<html>{{{body}}}</html>", body.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("doc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sms").ValueKind);
    }

    [Fact]
    public async Task Creating_an_email_template_persists_its_metadata_and_a_followup_get_reflects_it()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var baseId = await CreateBaseAsync(client);
        var name = Unique("email");

        var (id, _) = await CreateAsync(client, EmailBody(name, baseId));

        // Re-read via a second call rather than trusting the create response —
        // proves the child row actually persisted.
        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/v1/templates/{id}", CancellationToken.None);
        var email = fetched.GetProperty("email");
        Assert.Equal("Welcome", email.GetProperty("subject").GetString());
        Assert.Equal("noreply@api.test", email.GetProperty("fromEmail").GetString());
        Assert.Equal("HSM", email.GetProperty("fromName").GetString());
        Assert.Equal("cc@api.test", email.GetProperty("cc")[0].GetString());
        Assert.True(email.GetProperty("hasAttachment").GetBoolean());
        Assert.Equal(JsonValueKind.Null, fetched.GetProperty("doc").ValueKind);
        Assert.Equal(JsonValueKind.Null, fetched.GetProperty("sms").ValueKind);
    }

    [Fact]
    public async Task Creating_a_template_with_a_base_template_id_round_trips_it_on_get()
    {
        // A caller that creates a non-BASE template must be able to learn
        // which base it wraps back from GET — otherwise a UI can't render the
        // current value of a field it is expected to submit back on PUT.
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var baseId = await CreateBaseAsync(client);
        var name = Unique("email");
        var (id, created) = await CreateAsync(client, EmailBody(name, baseId));

        Assert.Equal(baseId, created.GetProperty("baseTemplateId").GetGuid());

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/v1/templates/{id}", CancellationToken.None);
        Assert.Equal(baseId, fetched.GetProperty("baseTemplateId").GetGuid());

        // A BASE template has no base of its own.
        var fetchedBase = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/templates/{baseId}", CancellationToken.None);
        Assert.Equal(JsonValueKind.Null, fetchedBase.GetProperty("baseTemplateId").ValueKind);
    }

    [Fact]
    public async Task Creating_a_docs_template_persists_its_metadata()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var baseId = await CreateBaseAsync(client);
        var name = Unique("doc");

        var (id, _) = await CreateAsync(client, DocBody(name, baseId));

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/v1/templates/{id}", CancellationToken.None);
        var doc = fetched.GetProperty("doc");
        Assert.Equal(DocumentCodes.All[0], doc.GetProperty("documentCode").GetString());
        Assert.Equal(DocumentFormats.All[0], doc.GetProperty("format").GetString());
        Assert.Equal(DocumentSizes.All[0], doc.GetProperty("size").GetString());
        Assert.Equal(DocumentOrientations.All[0], doc.GetProperty("orientation").GetString());
    }

    [Fact]
    public async Task Creating_an_sms_template_persists_its_metadata()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var baseId = await CreateBaseAsync(client);
        var name = Unique("sms");

        var (id, _) = await CreateAsync(client, SmsBody(name, baseId));

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/v1/templates/{id}", CancellationToken.None);
        var sms = fetched.GetProperty("sms");
        Assert.Equal("twilio", sms.GetProperty("provider").GetString());
        Assert.Equal("otp", sms.GetProperty("templateName").GetString());
        Assert.Equal("+10000000000", sms.GetProperty("from").GetString());
    }

    [Fact]
    public async Task Getting_a_template_by_its_name_returns_the_same_row_as_by_its_id()
    {
        // The deliberate asymmetry: GET's {id} has no :guid constraint because
        // GetTemplateQuery accepts the catalog's authored name as well as the
        // row's actual id.
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var name = Unique("base");
        var (id, _) = await CreateAsync(client, BaseBody(name));

        var byId = await client.GetFromJsonAsync<JsonElement>($"/api/v1/templates/{id}", CancellationToken.None);
        var byName = await client.GetFromJsonAsync<JsonElement>($"/api/v1/templates/{name}", CancellationToken.None);

        Assert.Equal(id, byId.GetProperty("id").GetGuid());
        Assert.Equal(id, byName.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task List_is_unpaged_and_omits_the_content_and_schema_of_every_item()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var name = Unique("base");
        await CreateAsync(client, BaseBody(name));

        var response = await client.GetAsync("/api/v1/templates", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Contains(
            list.EnumerateArray(), item => item.GetProperty("name").GetString() == name);
        var item = list.EnumerateArray().First(item => item.GetProperty("name").GetString() == name);
        Assert.False(item.TryGetProperty("content", out _));
        Assert.False(item.TryGetProperty("schema", out _));
    }

    [Fact]
    public async Task List_can_be_filtered_by_category()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var baseId = await CreateBaseAsync(client);
        var emailName = Unique("email");
        await CreateAsync(client, EmailBody(emailName, baseId));

        var response = await client.GetAsync(
            $"/api/v1/templates?category={TemplateCategories.EmailExternal}", CancellationToken.None);

        var list = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.All(
            list.EnumerateArray(),
            item => Assert.Equal(TemplateCategories.EmailExternal, item.GetProperty("category").GetString()));
        Assert.Contains(list.EnumerateArray(), item => item.GetProperty("name").GetString() == emailName);
    }

    [Fact]
    public async Task Updating_a_template_changes_its_fields_and_a_followup_get_reflects_them()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var (id, _) = await CreateAsync(client, BaseBody(Unique("base")));

        var response = await client.PutAsJsonAsync(
            $"/api/v1/templates/{id}",
            new { description = "updated description", isActive = false },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/v1/templates/{id}", CancellationToken.None);
        Assert.Equal("updated description", fetched.GetProperty("description").GetString());
        Assert.False(fetched.GetProperty("isActive").GetBoolean());
        // Category was not supplied — PATCH semantics leave it untouched.
        Assert.Equal(TemplateCategories.Base, fetched.GetProperty("category").GetString());
    }

    [Fact]
    public async Task Updating_a_templates_category_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var (id, _) = await CreateAsync(client, BaseBody(Unique("base")));

        var response = await client.PutAsJsonAsync(
            $"/api/v1/templates/{id}",
            new { category = TemplateCategories.EmailExternal },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("template", out _));
    }

    [Fact]
    public async Task Updating_an_unknown_template_id_is_a_404_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/templates/{Guid.NewGuid()}", new { description = "x" }, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    [Fact]
    public async Task Deleting_a_template_returns_204_and_a_followup_get_404s()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var (id, _) = await CreateAsync(client, BaseBody(Unique("base")));

        var response = await client.DeleteAsync($"/api/v1/templates/{id}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var fetched = await client.GetAsync($"/api/v1/templates/{id}", CancellationToken.None);
        await ProblemAssert.ProblemAsync(fetched, 404);
    }

    [Fact]
    public async Task Deleting_an_unknown_template_id_is_a_404_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.DeleteAsync($"/api/v1/templates/{Guid.NewGuid()}", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    [Fact]
    public async Task Deleting_a_template_referenced_as_a_base_is_a_409_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var baseId = await CreateBaseAsync(client);
        await CreateAsync(client, EmailBody(Unique("email"), baseId));

        var response = await client.DeleteAsync($"/api/v1/templates/{baseId}", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 409);
    }

    [Fact]
    public async Task Getting_an_unknown_template_id_is_a_404_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.GetAsync($"/api/v1/templates/{Guid.NewGuid()}", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    [Fact]
    public async Task Getting_an_unknown_template_name_is_a_404_problem()
    {
        // Proves the asymmetry the other way: a non-GUID id on GET still
        // reaches the handler (no :guid constraint) and answers a normal
        // problem+json 404 — unlike the same shape on PUT/DELETE below, which
        // never reach the handler at all.
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.GetAsync("/api/v1/templates/not-a-known-name", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    [Fact]
    public async Task A_non_guid_id_on_put_is_an_unmatched_route()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.PutAsJsonAsync(
            "/api/v1/templates/not-a-guid", new { description = "x" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_non_guid_id_on_delete_is_an_unmatched_route()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.DeleteAsync("/api/v1/templates/not-a-guid", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Creating_a_template_with_no_name_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.PostAsJsonAsync(
            "/api/v1/templates",
            new { category = TemplateCategories.Base, name = string.Empty, schema = new { }, content = "x" },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        // CreateTemplateValidator's RuleFor(x => x.Payload.Name) keys the
        // error by the full nested path — "payload.name", not "name".
        Assert.True(problem.GetProperty("errors").TryGetProperty("payload.name", out _));
    }

    [Fact]
    public async Task Creating_a_template_with_an_unknown_category_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.PostAsJsonAsync(
            "/api/v1/templates",
            new { category = "NOT_A_CATEGORY", name = Unique("bad"), schema = new { }, content = "x" },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("payload.category", out _));
    }

    [Fact]
    public async Task Validating_matching_data_returns_valid_true_and_no_issues()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var baseId = await CreateBaseAsync(client);
        var name = Unique("email");
        await CreateAsync(client, EmailBody(name, baseId));

        var response = await client.PostAsJsonAsync(
            "/api/v1/templates/validate",
            new { identifier = name, data = new { name = "Ada" } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.True(body.GetProperty("valid").GetBoolean());
        Assert.Equal(0, body.GetProperty("issues").GetArrayLength());
    }

    [Fact]
    public async Task Validating_data_that_fails_the_schema_returns_a_formatted_issue()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var baseId = await CreateBaseAsync(client);
        var name = Unique("email");
        await CreateAsync(client, EmailBody(name, baseId));

        var response = await client.PostAsJsonAsync(
            "/api/v1/templates/validate",
            new { identifier = name, data = new { } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.False(body.GetProperty("valid").GetBoolean());
        var issue = body.GetProperty("issues")[0];
        Assert.Equal("name", issue.GetProperty("path").GetString());
        Assert.Equal("expected string, got undefined", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Validating_an_unknown_identifier_is_a_404_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.PostAsJsonAsync(
            "/api/v1/templates/validate",
            new { identifier = "not-a-known-name", data = new { } },
            CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    [Fact]
    public async Task Draft_rendering_returns_the_rendered_html()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.PostAsJsonAsync(
            "/api/v1/templates/draft-render",
            new { content = "Hi {{name}}", baseTemplateId = (string?)null, sampleData = new { name = "World" } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal("Hi World", body.GetProperty("html").GetString());
    }

    [Fact]
    public async Task Draft_rendering_with_a_base_template_wraps_the_content()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var (baseId, _) = await CreateAsync(client, BaseBody(Unique("base"), "Base:{{{body}}}"));

        var response = await client.PostAsJsonAsync(
            "/api/v1/templates/draft-render",
            new { content = "Hi", baseTemplateId = baseId.ToString(), sampleData = new { } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal("Base:Hi", body.GetProperty("html").GetString());
    }

    [Fact]
    public async Task Draft_rendering_with_empty_content_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.PostAsJsonAsync(
            "/api/v1/templates/draft-render",
            new { content = string.Empty, baseTemplateId = (string?)null, sampleData = (object?)null },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("content", out _));
    }

    [Theory]
    [InlineData("GET", "/api/v1/templates")]
    [InlineData("POST", "/api/v1/templates")]
    [InlineData("POST", "/api/v1/templates/validate")]
    [InlineData("POST", "/api/v1/templates/draft-render")]
    [InlineData("GET", "/api/v1/templates/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/v1/templates/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/v1/templates/00000000-0000-0000-0000-000000000001")]
    public async Task Anonymous_callers_get_401_on_every_templates_route(string method, string route)
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
