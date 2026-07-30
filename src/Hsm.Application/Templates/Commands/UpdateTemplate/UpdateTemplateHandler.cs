using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Templates.Commands.CreateTemplate;
using Hsm.Application.Templates.Queries.GetTemplate;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Commands.UpdateTemplate;

public sealed class UpdateTemplateHandler(
    ITemplateStore store,
    ITemplateRenderer renderer,
    IAuthUnitOfWork unitOfWork,
    IRequestHandler<GetTemplateQuery, Template> reader)
    : IRequestHandler<UpdateTemplateCommand, Template>
{
    public async Task<Template> HandleAsync(UpdateTemplateCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = request.Payload;

        var existing = await store.FindByIdAsync(request.Id, withChildren: true, withBase: true, ct)
            ?? throw TemplateErrors.NotFound(request.Id.ToString());

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
        await unitOfWork.SaveChangesAsync(ct);

        return await reader.HandleAsync(new GetTemplateQuery(existing.Id.ToString()), ct);
    }

    /// <summary>The frozen upsertChildOnUpdate: keyed by the stored category.</summary>
    private static void UpsertChild(Template existing, TemplatePayload command)
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
