using System.Text.Json;
using System.Text.Json.Nodes;
using Hsm.Api.Http;
using Hsm.Application.Abstractions;
using Hsm.Application.Templates;
using Hsm.Application.Templates.Commands.CreateTemplate;
using Hsm.Application.Templates.Commands.DeleteTemplate;
using Hsm.Application.Templates.Commands.UpdateTemplate;
using Hsm.Application.Templates.Queries.DraftRender;
using Hsm.Application.Templates.Queries.GetTemplate;
using Hsm.Application.Templates.Queries.ListTemplates;
using Hsm.Application.Templates.Queries.ValidateTemplate;

namespace Hsm.Api.Templates;

/// <summary>
/// The templates resource. Every delegate does transport work only — bind,
/// dispatch, project, choose a status. There is no authentication call and no
/// role check here — the actor is installed by middleware and the policy
/// rides on the request type (every Templates command/query below is
/// authenticated-only, no role restriction).
///
/// <para><c>{id}</c> means two different things on this resource, and that is
/// deliberate, not an inconsistency to "fix": <see cref="GetTemplate"/> keeps
/// NO <c>:guid</c> route constraint, because <see cref="GetTemplateQuery"/>
/// accepts a slug — the catalog's <c>Name</c> column is the form templates are
/// authored in (see <see cref="TemplateResource"/>'s own doc comment) — and a
/// GUID-shaped value also matches by id (<c>TemplateStore.FindByIdentifierAsync</c>).
/// <see cref="UpdateTemplate"/>/<see cref="DeleteTemplate"/> DO constrain
/// <c>{id:guid}</c>, because they address a stored row by its actual primary
/// key, never by name.</para>
/// </summary>
public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var templates = app.MapGroup("/api/v1/templates").WithTags("Templates");

        templates.MapGet("/", ListTemplates)
            .WithSummary("List templates, optionally filtered by category (an unpaged catalog).")
            .Produces<IReadOnlyList<TemplateResource>>();

        templates.MapPost("/", CreateTemplate)
            .WithSummary("Create a template.")
            .Produces<TemplateDetailResource>(StatusCodes.Status201Created);

        templates.MapPost("/validate", ValidateTemplate)
            .WithSummary("Validate sample data against a stored template's schema and Handlebars source.")
            .Produces<ValidateTemplateResource>();

        templates.MapPost("/draft-render", DraftRender)
            .WithSummary("Render unsaved Handlebars source (optionally wrapped in a BASE template) against sample data.")
            .Produces<DraftRenderResource>();

        templates.MapGet("/{id}", GetTemplate)
            .WithSummary("Read one template, addressed by id or by its unique name.")
            .Produces<TemplateDetailResource>();

        templates.MapPut("/{id:guid}", UpdateTemplate)
            .WithSummary("Update a template.")
            .Produces<TemplateDetailResource>();

        templates.MapDelete("/{id:guid}", DeleteTemplate)
            .WithSummary("Delete a template not referenced as a base by any other template.")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> ListTemplates(
        IDispatcher dispatcher, CancellationToken ct, string? category = null)
    {
        var templates = await dispatcher.Send(new ListTemplatesQuery(category), ct);
        return Results.Ok(templates.Select(TemplateResource.From).ToList());
    }

    private static async Task<IResult> CreateTemplate(HttpContext ctx, IDispatcher dispatcher, CancellationToken ct)
    {
        var payload = await ReadTemplateBodyAsync(ctx);
        var created = await dispatcher.Send(new CreateTemplateCommand(payload), ct);
        return Results.Created($"/api/v1/templates/{created.Id}", TemplateDetailResource.From(created));
    }

    private static async Task<IResult> ValidateTemplate(
        ValidateTemplateRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.Send(new ValidateTemplateQuery(request.Identifier, request.Data), ct);
        var issues = (result.Issues ?? []).Select(TemplateIssueResource.From).ToList();
        return Results.Ok(new ValidateTemplateResource(result.Valid, result.TemplateId, issues));
    }

    private static async Task<IResult> DraftRender(
        DraftRenderRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        var html = await dispatcher.Send(
            new DraftRenderQuery(request.Content, request.BaseTemplateId, request.SampleData), ct);
        return Results.Ok(new DraftRenderResource(html));
    }

    private static async Task<IResult> GetTemplate(string id, IDispatcher dispatcher, CancellationToken ct) =>
        Results.Ok(TemplateDetailResource.From(await dispatcher.Send(new GetTemplateQuery(id), ct)));

    private static async Task<IResult> UpdateTemplate(
        Guid id, HttpContext ctx, IDispatcher dispatcher, CancellationToken ct)
    {
        var payload = await ReadTemplateBodyAsync(ctx);
        var updated = await dispatcher.Send(new UpdateTemplateCommand(id, payload), ct);
        return Results.Ok(TemplateDetailResource.From(updated));
    }

    private static async Task<IResult> DeleteTemplate(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        await dispatcher.Send(new DeleteTemplateCommand(id), ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Reads the Create/Update Template payload. Shape validation
    /// (required fields, category-conditional block requirements) does not
    /// happen here — CreateTemplateValidator/UpdateTemplateValidator carry the
    /// FluentValidation subset (Task 3), and CreateTemplateHandler's
    /// AssertCategoryShape carries the rest (Task 3's own note; a full
    /// FluentValidation port was never promised). DescriptionPresent/
    /// BaseTemplatePresent are computed from the raw JSON keys because the
    /// handlers use them for PATCH semantics: a field present-but-null clears
    /// the column; an absent field leaves it alone — a plain record binder
    /// cannot distinguish those two cases, which is why this endpoint still
    /// reads the body as a bare <see cref="JsonObject"/> instead of a typed
    /// request record like every other reshaped module.
    /// </summary>
    private static async Task<TemplatePayload> ReadTemplateBodyAsync(HttpContext ctx)
    {
        var body = await ctx.Request.ReadValidatedJsonAsync<JsonObject>(ctx.RequestAborted) ?? [];

        var category = OptionalString(body, "category");
        var name = OptionalString(body, "name");
        var descriptionPresent = body.ContainsKey("description");
        var description = OptionalString(body, "description");
        var isActive = OptionalBool(body, "isActive");
        var schema = body["schema"];
        var content = OptionalString(body, "content");
        var basePresent = body.ContainsKey("baseTemplateId");
        var baseTemplateId = OptionalString(body, "baseTemplateId");

        var email = ReadEmailBlock(body);
        var doc = ReadDocBlock(body);
        var sms = ReadSmsBlock(body);

        return new TemplatePayload(
            category, name, description, descriptionPresent, isActive, schema,
            content, baseTemplateId, basePresent, email, doc, sms);
    }

    private static EmailShape? ReadEmailBlock(JsonObject body)
    {
        if (body["email"] is not JsonObject block)
        {
            return null;
        }

        return new EmailShape(
            OptionalString(block, "subject") ?? string.Empty,
            OptionalString(block, "fromEmail") ?? string.Empty,
            OptionalString(block, "fromName") ?? string.Empty,
            OptionalStringArray(block, "cc"),
            OptionalStringArray(block, "bcc"),
            OptionalBool(block, "hasAttachment"));
    }

    private static DocShape? ReadDocBlock(JsonObject body)
    {
        if (body["doc"] is not JsonObject block)
        {
            return null;
        }

        return new DocShape(
            OptionalString(block, "documentCode") ?? string.Empty,
            OptionalString(block, "format") ?? string.Empty,
            OptionalString(block, "size") ?? string.Empty,
            OptionalString(block, "orientation") ?? string.Empty);
    }

    private static SmsShape? ReadSmsBlock(JsonObject body)
    {
        if (body["sms"] is not JsonObject block)
        {
            return null;
        }

        return new SmsShape(
            OptionalString(block, "provider") ?? string.Empty,
            OptionalString(block, "templateName") ?? string.Empty,
            OptionalString(block, "from") ?? string.Empty);
    }

    private static string? OptionalString(JsonObject body, string field) =>
        body[field]?.GetValueKind() == JsonValueKind.String ? body[field]!.GetValue<string>() : null;

    private static bool? OptionalBool(JsonObject body, string field) =>
        body[field]?.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

    private static List<string>? OptionalStringArray(JsonObject body, string field) =>
        body[field] is JsonArray array
            ? [.. array.Select(n => n?.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : string.Empty)]
            : null;
}
