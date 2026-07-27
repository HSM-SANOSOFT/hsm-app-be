using System.Text.Json.Nodes;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates;

/// <summary>The create/update payload as seen by the handlers (frozen CreateTemplatePayloadDto).</summary>
public sealed record TemplateCommand(
    string? Category,
    string? Name,
    string? Description,
    bool DescriptionPresent,
    bool? IsActive,
    JsonNode? Schema,
    string? Content,
    string? BaseTemplateId,
    bool BaseTemplatePresent,
    EmailShape? Email,
    DocShape? Doc,
    SmsShape? Sms);

/// <summary>The frozen EmailTemplateFieldsDto.</summary>
public sealed record EmailShape(
    string Subject, string FromEmail, string FromName,
    IReadOnlyList<string>? Cc, IReadOnlyList<string>? Bcc, bool? HasAttachment);

/// <summary>The frozen DocTemplateFieldsDto.</summary>
public sealed record DocShape(string DocumentCode, string Format, string Size, string Orientation);

/// <summary>The frozen SmsTemplateFieldsDto.</summary>
public sealed record SmsShape(string Provider, string TemplateName, string From);

/// <summary>Shared error factories mirroring the frozen templates.error.ts family.</summary>
public static class TemplateErrors
{
    public static ApiException NotFound(string identifier) =>
        ApiException.NotFound($"Template '{identifier}' not found");

    public static ApiException AlreadyExists(string name) =>
        new(409, $"Template with name '{name}' already exists", errorLabel: "Conflict");

    public static ApiException InUse(Guid id) =>
        new(
            409,
            $"Template '{id}' is referenced as a base by other templates and cannot be deleted",
            errorLabel: "Conflict");

    public static ApiException InvalidShape(string reason) =>
        ApiException.BadRequest($"Invalid template payload: {reason}");

    public static ApiException InvalidHandlebars(string message) =>
        ApiException.BadRequest($"Invalid Handlebars template: {message}");
}

/// <summary>Frozen findAll: every template (optionally category-filtered), name ASC, children loaded.</summary>
public sealed class ListTemplatesHandler(ITemplateStore store)
{
    public Task<IReadOnlyList<Template>> HandleAsync(string? category, CancellationToken ct = default) =>
        store.ListAsync(category, ct);
}

/// <summary>Frozen findByIdentifier: id-or-name lookup with children and base loaded.</summary>
public sealed class GetTemplateHandler(ITemplateStore store)
{
    public async Task<Template> HandleAsync(string identifier, CancellationToken ct = default) =>
        await store.FindByIdentifierAsync(identifier, withChildren: true, withBase: true, ct)
            ?? throw TemplateErrors.NotFound(identifier);
}

/// <summary>
/// Frozen create: category-shape assertion, schema well-formedness, Handlebars
/// compile check, BASE-reference validation, name uniqueness (409), then
/// parent + child row in one transaction.
/// </summary>
public sealed class CreateTemplateHandler(
    ITemplateStore store,
    ITemplateRenderer renderer,
    IAuthUnitOfWork unitOfWork,
    GetTemplateHandler reader)
{
    public async Task<Template> HandleAsync(TemplateCommand command, CancellationToken ct = default)
    {
        var category = command.Category ?? string.Empty;
        AssertCategoryShape(command);

        if (!TemplateSchema.IsWellFormed(command.Schema))
        {
            throw TemplateErrors.InvalidShape(
                "schema is malformed: leaves must be a known type tag, objects, or single-element arrays");
        }

        AssertCompiles(command.Content ?? string.Empty);

        Guid? baseTemplateId = null;
        if (!string.IsNullOrEmpty(command.BaseTemplateId))
        {
            baseTemplateId = (await ResolveBaseAsync(store, command.BaseTemplateId, ct)).Id;
        }

        if (await store.NameExistsAsync(command.Name ?? string.Empty, excludeId: null, ct))
        {
            throw TemplateErrors.AlreadyExists(command.Name ?? string.Empty);
        }

        var template = new Template
        {
            Id = Guid.NewGuid(),
            Category = category,
            Name = command.Name ?? string.Empty,
            Description = command.Description,
            IsActive = command.IsActive ?? true,
            SchemaJson = (command.Schema ?? new JsonObject()).ToJsonString(),
            Content = command.Content ?? string.Empty,
            BaseTemplateId = baseTemplateId,
        };
        ApplyChild(template, command);

        await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                await store.AddAsync(template, innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return true;
            },
            ct);

        // Frozen behavior: respond with the fresh read-back (children + base).
        return await reader.HandleAsync(template.Id.ToString(), ct);
    }

    /// <summary>The frozen assertCategoryShape, message for message.</summary>
    internal static void AssertCategoryShape(TemplateCommand command)
    {
        var category = command.Category;
        if (string.IsNullOrEmpty(category))
        {
            return;
        }

        if (category == TemplateCategories.Base)
        {
            if (!string.IsNullOrEmpty(command.BaseTemplateId))
            {
                throw TemplateErrors.InvalidShape("BASE templates must not have baseTemplateId");
            }

            if (command.Email is not null || command.Doc is not null || command.Sms is not null)
            {
                throw TemplateErrors.InvalidShape("BASE templates must not include email, doc, or sms fields");
            }

            return;
        }

        if (string.IsNullOrEmpty(command.BaseTemplateId))
        {
            throw TemplateErrors.InvalidShape($"{category} templates require baseTemplateId");
        }

        if (TemplateCategories.IsEmail(category))
        {
            if (command.Email is null)
            {
                throw TemplateErrors.InvalidShape($"{category} templates require the email block");
            }

            if (command.Doc is not null || command.Sms is not null)
            {
                throw TemplateErrors.InvalidShape($"{category} templates must not include the doc or sms block");
            }
        }

        if (category == TemplateCategories.Docs)
        {
            if (command.Doc is null)
            {
                throw TemplateErrors.InvalidShape("DOCS templates require the doc block");
            }

            if (command.Email is not null || command.Sms is not null)
            {
                throw TemplateErrors.InvalidShape("DOCS templates must not include the email or sms block");
            }
        }

        if (TemplateCategories.IsSms(category))
        {
            if (command.Sms is null)
            {
                throw TemplateErrors.InvalidShape($"{category} templates require the sms block");
            }

            if (command.Email is not null || command.Doc is not null)
            {
                throw TemplateErrors.InvalidShape($"{category} templates must not include the email or doc block");
            }
        }
    }

    /// <summary>Resolves a baseTemplateId: it must exist (404) and be BASE (400).</summary>
    internal static async Task<Template> ResolveBaseAsync(
        ITemplateStore store, string baseTemplateId, CancellationToken ct)
    {
        var template = Guid.TryParse(baseTemplateId, out var id)
            ? await store.FindByIdAsync(id, withChildren: false, withBase: false, ct)
            : null;
        if (template is null)
        {
            throw TemplateErrors.NotFound(baseTemplateId);
        }

        if (template.Category != TemplateCategories.Base)
        {
            throw TemplateErrors.InvalidShape("baseTemplateId must reference a template with category=BASE");
        }

        return template;
    }

    internal static void ApplyChild(Template template, TemplateCommand command)
    {
        if (command.Email is not null && TemplateCategories.IsEmail(template.Category))
        {
            template.Email = new TemplateEmail
            {
                Id = template.Id,
                Subject = command.Email.Subject,
                FromEmail = command.Email.FromEmail,
                FromName = command.Email.FromName,
                Cc = command.Email.Cc is null ? null : [.. command.Email.Cc],
                Bcc = command.Email.Bcc is null ? null : [.. command.Email.Bcc],
                HasAttachment = command.Email.HasAttachment ?? false,
            };
        }

        if (command.Doc is not null && template.Category == TemplateCategories.Docs)
        {
            template.Doc = new TemplateDoc
            {
                Id = template.Id,
                DocumentCode = command.Doc.DocumentCode,
                Format = command.Doc.Format,
                Size = command.Doc.Size,
                Orientation = command.Doc.Orientation,
            };
        }

        if (command.Sms is not null && TemplateCategories.IsSms(template.Category))
        {
            template.Sms = new TemplateSms
            {
                Id = template.Id,
                Provider = command.Sms.Provider,
                TemplateName = command.Sms.TemplateName,
                From = command.Sms.From,
            };
        }
    }

    private void AssertCompiles(string content)
    {
        try
        {
            renderer.AssertCompiles(content);
        }
        catch (TemplateRenderException exception)
        {
            throw TemplateErrors.InvalidHandlebars(exception.Message);
        }
    }
}

/// <summary>
/// Frozen update: category immutable, per-field patches, child block upserted
/// only when supplied and matching the STORED category.
/// </summary>
public sealed class UpdateTemplateHandler(
    ITemplateStore store,
    ITemplateRenderer renderer,
    IAuthUnitOfWork unitOfWork,
    GetTemplateHandler reader)
{
    public async Task<Template> HandleAsync(Guid id, TemplateCommand command, CancellationToken ct = default)
    {
        var existing = await store.FindByIdAsync(id, withChildren: true, withBase: true, ct)
            ?? throw TemplateErrors.NotFound(id.ToString());

        if (command.Category is not null && command.Category != existing.Category)
        {
            throw TemplateErrors.InvalidShape("category is immutable; create a new template instead");
        }

        if (command.Content is not null)
        {
            try
            {
                renderer.AssertCompiles(command.Content);
            }
            catch (TemplateRenderException exception)
            {
                throw TemplateErrors.InvalidHandlebars(exception.Message);
            }
        }

        if (command.Schema is not null && !TemplateSchema.IsWellFormed(command.Schema))
        {
            throw TemplateErrors.InvalidShape("schema is malformed");
        }

        if (command.Name is not null && command.Name != existing.Name
            && await store.NameExistsAsync(command.Name, excludeId: existing.Id, ct))
        {
            throw TemplateErrors.AlreadyExists(command.Name);
        }

        Guid? newBaseId = existing.BaseTemplateId;
        if (!string.IsNullOrEmpty(command.BaseTemplateId)
            && command.BaseTemplateId != existing.BaseTemplateId?.ToString())
        {
            newBaseId = (await CreateTemplateHandler.ResolveBaseAsync(store, command.BaseTemplateId, ct)).Id;
        }
        else if (command.BaseTemplatePresent && string.IsNullOrEmpty(command.BaseTemplateId))
        {
            // Frozen: an explicitly-present null clears the relation.
            newBaseId = null;
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                if (command.Name is not null)
                {
                    existing.Name = command.Name;
                }

                if (command.DescriptionPresent)
                {
                    existing.Description = command.Description;
                }

                if (command.IsActive is not null)
                {
                    existing.IsActive = command.IsActive.Value;
                }

                if (command.Schema is not null)
                {
                    existing.SchemaJson = command.Schema.ToJsonString();
                }

                if (command.Content is not null)
                {
                    existing.Content = command.Content;
                }

                existing.BaseTemplateId = newBaseId;
                // Base navigation may now be stale; the read-back reloads it.
                existing.BaseTemplate = null;

                UpsertChild(existing, command);
                await unitOfWork.SaveChangesAsync(innerCt);
                return true;
            },
            ct);

        return await reader.HandleAsync(existing.Id.ToString(), ct);
    }

    /// <summary>The frozen upsertChildOnUpdate: keyed by the stored category.</summary>
    private static void UpsertChild(Template existing, TemplateCommand command)
    {
        if (TemplateCategories.IsEmail(existing.Category) && command.Email is not null)
        {
            existing.Email ??= new TemplateEmail { Id = existing.Id };
            existing.Email.Subject = command.Email.Subject;
            existing.Email.FromEmail = command.Email.FromEmail;
            existing.Email.FromName = command.Email.FromName;
            existing.Email.Cc = command.Email.Cc is null ? null : [.. command.Email.Cc];
            existing.Email.Bcc = command.Email.Bcc is null ? null : [.. command.Email.Bcc];
            existing.Email.HasAttachment = command.Email.HasAttachment ?? false;
        }

        if (existing.Category == TemplateCategories.Docs && command.Doc is not null)
        {
            existing.Doc ??= new TemplateDoc { Id = existing.Id };
            existing.Doc.DocumentCode = command.Doc.DocumentCode;
            existing.Doc.Format = command.Doc.Format;
            existing.Doc.Size = command.Doc.Size;
            existing.Doc.Orientation = command.Doc.Orientation;
        }

        if (TemplateCategories.IsSms(existing.Category) && command.Sms is not null)
        {
            existing.Sms ??= new TemplateSms { Id = existing.Id };
            existing.Sms.Provider = command.Sms.Provider;
            existing.Sms.TemplateName = command.Sms.TemplateName;
            existing.Sms.From = command.Sms.From;
        }
    }
}

/// <summary>
/// Frozen delete: 404 when missing, and the in-use invariant surfaces as the
/// DOMAIN 409 (never a database foreign-key error): a template referenced as a
/// base by other templates cannot be deleted.
/// </summary>
public sealed class DeleteTemplateHandler(ITemplateStore store, IAuthUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid id, CancellationToken ct = default)
    {
        var target = await store.FindByIdAsync(id, withChildren: true, withBase: false, ct)
            ?? throw TemplateErrors.NotFound(id.ToString());

        if (await store.CountReferencingBaseAsync(id, ct) > 0)
        {
            throw TemplateErrors.InUse(id);
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                await store.RemoveAsync(target, innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return true;
            },
            ct);
    }
}

/// <summary>The validate result (frozen ValidateTemplateResponseDto).</summary>
public sealed record ValidateTemplateResult(
    bool Valid, Guid? TemplateId, IReadOnlyList<TemplateSchemaIssue>? Issues);

/// <summary>
/// Frozen validate: schema check then a compile probe — always a SUCCESS
/// response carrying valid/issues, never an error status (except unknown
/// identifier, which is 404). Writes no parse log.
/// </summary>
public sealed class ValidateTemplateHandler(ITemplateStore store, ITemplateRenderer renderer)
{
    public async Task<ValidateTemplateResult> HandleAsync(
        string identifier, JsonNode? data, CancellationToken ct = default)
    {
        var template = await store.FindByIdentifierAsync(identifier, withChildren: false, withBase: false, ct)
            ?? throw TemplateErrors.NotFound(identifier);

        var issues = TemplateSchema.Validate(JsonNode.Parse(template.SchemaJson), data);
        if (issues.Count > 0)
        {
            return new ValidateTemplateResult(Valid: false, template.Id, issues);
        }

        try
        {
            renderer.AssertCompiles(template.Content);
        }
        catch (TemplateRenderException exception)
        {
            return new ValidateTemplateResult(
                Valid: false,
                template.Id,
                [new TemplateSchemaIssue("content", "compilable Handlebars", exception.Message)]);
        }

        return new ValidateTemplateResult(Valid: true, template.Id, Issues: null);
    }
}

/// <summary>
/// Frozen draftRender: compose unsaved Handlebars source (optionally wrapped
/// in a BASE template) against sample data. Nothing is persisted or logged.
/// </summary>
public sealed class DraftRenderHandler(ITemplateStore store, ITemplateRenderer renderer)
{
    public async Task<string> HandleAsync(
        string content, string? baseTemplateId, JsonObject? sampleData, CancellationToken ct = default)
    {
        string? baseContent = null;
        if (!string.IsNullOrEmpty(baseTemplateId))
        {
            baseContent = (await CreateTemplateHandler.ResolveBaseAsync(store, baseTemplateId, ct)).Content;
        }

        try
        {
            return renderer.Render(content, baseContent, sampleData ?? []);
        }
        catch (TemplateRenderException exception)
        {
            throw TemplateErrors.InvalidHandlebars(exception.Message);
        }
    }
}
