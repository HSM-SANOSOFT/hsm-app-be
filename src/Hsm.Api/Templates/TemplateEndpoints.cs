using System.Text.Json;
using System.Text.Json.Nodes;
using Hsm.Api.Auth;
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
using Hsm.Domain.Templates;

namespace Hsm.Api.Templates;

/// <summary>
/// The seven frozen /v1/templates operations (templates.controller.ts). Every
/// route is @Roles() with no arguments — any authenticated, onboarded user.
/// POST routes return 201 (the frozen runtime default; its OpenAPI snapshot
/// under-documented these as 200). The category-conditional nested-block shape
/// rules (email/doc/sms requirements, schema/Handlebars checks) are a full
/// reshape left to Task 8 — Task 3 only ports the business rules in Step 6's
/// table (name/category non-empty, category known); a malformed nested block
/// now degrades to empty/default values instead of a field-keyed 400.
/// </summary>
public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var templates = app.MapGroup("/v1/templates");
        templates.MapGet("", (Delegate)ListTemplates);
        templates.MapPost("", (Delegate)CreateTemplate);
        templates.MapPost("/validate", (Delegate)ValidateTemplate);
        templates.MapPost("/draft-render", (Delegate)DraftRender);
        templates.MapGet("/{identifier}", (Delegate)GetTemplate);
        templates.MapPut("/{id}", (Delegate)UpdateTemplate);
        templates.MapDelete("/{id}", (Delegate)DeleteTemplate);
    }

    private static async Task<IResult> ListTemplates(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var category = ctx.Request.Query.TryGetValue("category", out var values) ? values[^1] : null;

        var templates = await dispatcher.Send(new ListTemplatesQuery(category), ctx.RequestAborted);
        var data = new JsonArray([.. templates.Select(t => (JsonNode?)DetailJson(t))]);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, data, extra: ApiEnvelope.SinglePagePagination(templates.Count));
    }

    private static async Task<IResult> GetTemplate(
        HttpContext ctx, string identifier, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var template = await dispatcher.Send(new GetTemplateQuery(identifier), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, WithBaseJson(template));
    }

    private static async Task<IResult> CreateTemplate(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var payload = await ReadTemplateBodyAsync(ctx);
        var created = await dispatcher.Send(new CreateTemplateCommand(payload), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, WithBaseJson(created));
    }

    private static async Task<IResult> UpdateTemplate(
        HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var templateId = Guid.Parse(id);
        var payload = await ReadTemplateBodyAsync(ctx);
        var updated = await dispatcher.Send(
            new UpdateTemplateCommand(templateId, payload), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, WithBaseJson(updated));
    }

    private static async Task<IResult> DeleteTemplate(
        HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var templateId = Guid.Parse(id);
        await dispatcher.Send(new DeleteTemplateCommand(templateId), ctx.RequestAborted);
        // Frozen controller: delete answers { id } (enveloped), 200.
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, new JsonObject { ["id"] = templateId.ToString() });
    }

    private static async Task<IResult> ValidateTemplate(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var query = await ctx.Request.ReadValidatedJsonAsync<ValidateTemplateQuery>(ctx.RequestAborted)
            ?? new ValidateTemplateQuery(string.Empty, null);

        var result = await dispatcher.Send(query, ctx.RequestAborted);
        var json = new JsonObject { ["valid"] = result.Valid };
        if (result.TemplateId is not null)
        {
            json["templateId"] = result.TemplateId.ToString();
        }

        if (result.Issues is not null)
        {
            json["issues"] = IssuesJson(result.Issues);
        }

        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, json);
    }

    private static async Task<IResult> DraftRender(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var query = await ctx.Request.ReadValidatedJsonAsync<DraftRenderQuery>(ctx.RequestAborted)
            ?? new DraftRenderQuery(string.Empty, null, null);

        var html = await dispatcher.Send(query, ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status201Created, new JsonObject { ["html"] = html });
    }

    internal static JsonArray IssuesJson(IReadOnlyList<TemplateSchemaIssue> issues) =>
        new([.. issues.Select(issue => (JsonNode?)new JsonObject
        {
            ["path"] = issue.Path,
            ["expected"] = issue.Expected,
            ["received"] = issue.Received,
        })]);

    /// <summary>The frozen TemplateWithBaseResponseDto shape.</summary>
    internal static JsonObject WithBaseJson(Template template) => new()
    {
        ["template"] = DetailJson(template),
        ["baseTemplate"] = template.BaseTemplate is null ? null : DetailJson(template.BaseTemplate),
    };

    /// <summary>
    /// The frozen TemplateDetailDto: metadata is the category-matching shape
    /// (cc/bcc omitted when unset), null for BASE or when no child row exists.
    /// </summary>
    internal static JsonObject DetailJson(Template template)
    {
        JsonObject? metadata = null;
        if (TemplateCategories.IsEmail(template.Category) && template.Email is not null)
        {
            metadata = new JsonObject
            {
                ["subject"] = template.Email.Subject,
                ["fromEmail"] = template.Email.FromEmail,
                ["fromName"] = template.Email.FromName,
            };
            if (template.Email.Cc is not null)
            {
                metadata["cc"] = new JsonArray([.. template.Email.Cc.Select(c => (JsonNode?)c)]);
            }

            if (template.Email.Bcc is not null)
            {
                metadata["bcc"] = new JsonArray([.. template.Email.Bcc.Select(b => (JsonNode?)b)]);
            }

            metadata["hasAttachment"] = template.Email.HasAttachment;
        }
        else if (template.Category == TemplateCategories.Docs && template.Doc is not null)
        {
            metadata = new JsonObject
            {
                ["documentCode"] = template.Doc.DocumentCode,
                ["format"] = template.Doc.Format,
                ["size"] = template.Doc.Size,
                ["orientation"] = template.Doc.Orientation,
            };
        }
        else if (TemplateCategories.IsSms(template.Category) && template.Sms is not null)
        {
            metadata = new JsonObject
            {
                ["provider"] = template.Sms.Provider,
                ["templateName"] = template.Sms.TemplateName,
                ["from"] = template.Sms.From,
            };
        }

        return new JsonObject
        {
            ["id"] = template.Id.ToString(),
            ["category"] = template.Category,
            ["name"] = template.Name,
            ["isActive"] = template.IsActive,
            ["schema"] = JsonNode.Parse(template.SchemaJson),
            ["content"] = template.Content,
            ["description"] = template.Description,
            ["metadata"] = metadata,
        };
    }

    /// <summary>
    /// Reads the frozen Create/Update Template payload. Shape validation
    /// (required fields, category-conditional block requirements) no longer
    /// happens here — CreateTemplateValidator/UpdateTemplateValidator carry
    /// the subset ported in Task 3, and the rest is a Task 8 reshape.
    /// DescriptionPresent/BaseTemplatePresent are still computed from the raw
    /// JSON keys because the handlers use them for PATCH semantics (a field
    /// present-but-null clears the column; an absent field leaves it alone).
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
