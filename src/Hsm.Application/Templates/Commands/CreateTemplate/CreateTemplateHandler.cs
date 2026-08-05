using System.Text.Json.Nodes;
using Hsm.Application.Abstractions;
using Hsm.Application.Identity;
using Hsm.Application.Templates.Queries.GetTemplate;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Commands.CreateTemplate;

public sealed class CreateTemplateHandler(
    ITemplateStore store,
    ITemplateRenderer renderer,
    IUnitOfWork unitOfWork,
    IRequestHandler<GetTemplateQuery, Template> reader)
    : IRequestHandler<CreateTemplateCommand, Template>
{
    public async Task<Template> HandleAsync(CreateTemplateCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = request.Payload;

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

        await store.AddAsync(template, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Respond with the fresh read-back (children + base).
        return await reader.HandleAsync(new GetTemplateQuery(template.Id.ToString()), ct);
    }

    /// <summary>Validates category-conditional shape requirements for the payload.</summary>
    internal static void AssertCategoryShape(TemplatePayload command)
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

    internal static void ApplyChild(Template template, TemplatePayload command)
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
