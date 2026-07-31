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
/// under-documented these as 200).
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

        var query = QueryValidator.Read(ctx);
        var category = query.OptionalEnum("category", TemplateCategories.All);
        query.RejectUnknownParams();
        query.ThrowIfInvalid();

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
        var command = await ReadTemplateBodyAsync(ctx, isCreate: true);
        var created = await dispatcher.Send(new CreateTemplateCommand(command), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, WithBaseJson(created));
    }

    private static async Task<IResult> UpdateTemplate(
        HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var templateId = RouteParams.PipedUuid(id);
        var command = await ReadTemplateBodyAsync(ctx, isCreate: false);
        var updated = await dispatcher.Send(
            new UpdateTemplateCommand(templateId, command), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, WithBaseJson(updated));
    }

    private static async Task<IResult> DeleteTemplate(
        HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var templateId = RouteParams.PipedUuid(id);
        await dispatcher.Send(new DeleteTemplateCommand(templateId), ctx.RequestAborted);
        // Frozen controller: delete answers { id } (enveloped), 200.
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, new JsonObject { ["id"] = templateId.ToString() });
    }

    private static async Task<IResult> ValidateTemplate(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var identifier = body.RequiredString("identifier");
        var data = body.RequiredObject("data");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var result = await dispatcher.Send(new ValidateTemplateQuery(identifier, data), ctx.RequestAborted);
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

        var body = await BodyValidator.ReadAsync(ctx);
        var content = body.RequiredString("content");
        var baseTemplateId = body.OptionalUuid("baseTemplateId");
        var sampleData = body.OptionalObject("sampleData");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var html = await dispatcher.Send(
            new DraftRenderQuery(content, baseTemplateId, sampleData), ctx.RequestAborted);
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
    /// Reads the frozen Create/Update Template payload, reproducing the
    /// ValidationPipe surface: field rules, the @ValidateIf conditional
    /// requirements keyed on the payload's own category, nested-block
    /// validation, and whitelist enforcement.
    /// </summary>
    private static async Task<TemplatePayload> ReadTemplateBodyAsync(HttpContext ctx, bool isCreate)
    {
        var body = await BodyValidator.ReadAsync(ctx);

        string? category = null;
        if (isCreate || body.Has("category"))
        {
            category = body.RequiredEnumNotEmpty("category", TemplateCategories.All);
        }

        var name = isCreate
            ? body.RequiredString("name")
            : body.OptionalString("name", notEmpty: true);
        var descriptionPresent = body.Has("description");
        var description = body.OptionalString("description");
        var isActive = body.OptionalBool("isActive");
        var schema = isCreate ? body.RequiredObject("schema") : (JsonNode?)body.OptionalObject("schema");
        var content = isCreate
            ? body.RequiredString("content")
            : body.OptionalString("content", notEmpty: true);

        // Frozen @ValidateIf(category !== BASE) + @IsNotEmpty + @IsUUID on
        // create; PartialType makes it optional on update.
        string? baseTemplateId = null;
        var basePresent = body.Has("baseTemplateId");
        if (isCreate && category != TemplateCategories.Base)
        {
            var node = body.RawNode("baseTemplateId");
            if (node is null
                || node.GetValueKind() != JsonValueKind.String
                || node.GetValue<string>().Length == 0)
            {
                body.AddFailure("baseTemplateId", "isNotEmpty", "baseTemplateId should not be empty");
                body.AddFailure("baseTemplateId", "isUuid", "baseTemplateId must be a UUID");
            }
            else if (!Guid.TryParse(node.GetValue<string>(), out _))
            {
                body.AddFailure("baseTemplateId", "isUuid", "baseTemplateId must be a UUID");
            }
            else
            {
                baseTemplateId = node.GetValue<string>();
            }
        }
        else
        {
            baseTemplateId = body.OptionalUuid("baseTemplateId");
        }

        var email = ReadEmailBlock(body, category, isCreate);
        var doc = ReadDocBlock(body, category, isCreate);
        var sms = ReadSmsBlock(body, category, isCreate);

        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        return new TemplatePayload(
            category, name, description, descriptionPresent, isActive, schema,
            content, baseTemplateId, basePresent, email, doc, sms);
    }

    private static EmailShape? ReadEmailBlock(
        BodyValidator body, string? category, bool isCreate)
    {
        var node = body.RawNode("email");
        var required = isCreate && category is not null && TemplateCategories.IsEmail(category);
        if (node is null)
        {
            if (required)
            {
                body.AddFailure("email", "isNotEmpty", "email should not be empty");
            }

            return null;
        }

        if (node is not JsonObject block)
        {
            body.AddFailure(
                "email", "nestedValidation", "nested property email must be either object or array");
            return null;
        }

        var scope = body.Scope(block, "email");
        var subject = scope.NonEmptyString("subject");
        var fromEmail = scope.Email("fromEmail");
        var fromName = scope.NonEmptyString("fromName");
        var cc = OptionalNestedEmailArray(body, block, "email", "cc");
        var bcc = OptionalNestedEmailArray(body, block, "email", "bcc");
        bool? hasAttachment = null;
        var attachmentNode = block["hasAttachment"];
        if (attachmentNode is not null)
        {
            var kind = attachmentNode.GetValueKind();
            if (kind is not (JsonValueKind.True or JsonValueKind.False))
            {
                body.AddFailure(
                    "email.hasAttachment", "isBoolean", "email.hasAttachment must be a boolean value");
            }
            else
            {
                hasAttachment = kind == JsonValueKind.True;
            }
        }

        RejectUnknownNested(body, block, "email", ["subject", "fromEmail", "fromName", "cc", "bcc", "hasAttachment"]);
        return new EmailShape(subject, fromEmail, fromName, cc, bcc, hasAttachment);
    }

    private static DocShape? ReadDocBlock(
        BodyValidator body, string? category, bool isCreate)
    {
        var node = body.RawNode("doc");
        var required = isCreate && category == TemplateCategories.Docs;
        if (node is null)
        {
            if (required)
            {
                body.AddFailure("doc", "isNotEmpty", "doc should not be empty");
            }

            return null;
        }

        if (node is not JsonObject block)
        {
            body.AddFailure("doc", "nestedValidation", "nested property doc must be either object or array");
            return null;
        }

        var scope = body.Scope(block, "doc");
        var documentCode = scope.Enum("documentCode", DocumentCodes.All);
        var format = scope.Enum("format", DocumentFormats.All);
        var size = scope.Enum("size", DocumentSizes.All);
        var orientation = scope.Enum("orientation", DocumentOrientations.All);
        RejectUnknownNested(body, block, "doc", ["documentCode", "format", "size", "orientation"]);
        return new DocShape(documentCode, format, size, orientation);
    }

    private static SmsShape? ReadSmsBlock(
        BodyValidator body, string? category, bool isCreate)
    {
        var node = body.RawNode("sms");
        var required = isCreate && category is not null && TemplateCategories.IsSms(category);
        if (node is null)
        {
            if (required)
            {
                body.AddFailure("sms", "isNotEmpty", "sms should not be empty");
            }

            return null;
        }

        if (node is not JsonObject block)
        {
            body.AddFailure("sms", "nestedValidation", "nested property sms must be either object or array");
            return null;
        }

        var scope = body.Scope(block, "sms");
        var provider = scope.NonEmptyString("provider");
        var templateName = scope.NonEmptyString("templateName");
        var from = scope.NonEmptyString("from");
        RejectUnknownNested(body, block, "sms", ["provider", "templateName", "from"]);
        return new SmsShape(provider, templateName, from);
    }

    private static List<string>? OptionalNestedEmailArray(
        BodyValidator body, JsonObject block, string parent, string field)
    {
        var node = block[field];
        if (node is null)
        {
            return null;
        }

        if (node is not JsonArray array || array.Count == 0)
        {
            body.AddFailure($"{parent}.{field}", "arrayNotEmpty", $"{parent}.{field} should not be empty");
            return null;
        }

        var items = new List<string>();
        foreach (var item in array)
        {
            if (item is null
                || item.GetValueKind() != JsonValueKind.String
                || !item.GetValue<string>().Contains('@', StringComparison.Ordinal))
            {
                body.AddFailure(
                    $"{parent}.{field}", "isEmail", $"each value in {parent}.{field} must be an email");
                return null;
            }

            items.Add(item.GetValue<string>());
        }

        return items;
    }

    private static void RejectUnknownNested(
        BodyValidator body, JsonObject block, string parent, IReadOnlyList<string> known)
    {
        foreach (var property in block)
        {
            if (!known.Contains(property.Key, StringComparer.Ordinal))
            {
                body.AddFailure(
                    $"{parent}.{property.Key}", "whitelistValidation",
                    $"property {property.Key} should not exist");
            }
        }
    }
}
