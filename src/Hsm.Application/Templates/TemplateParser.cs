using System.Text.Json.Nodes;
using Hsm.Application.Auth;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates;

/// <summary>Raised when a parse's data fails the template schema (frozen TemplateSchemaValidationError).</summary>
public sealed class TemplateParseSchemaException(IReadOnlyList<TemplateSchemaIssue> issues)
    : Exception("Template schema validation failed")
{
    public IReadOnlyList<TemplateSchemaIssue> Issues { get; } = issues;
}

/// <summary>Raised when a parsed template is missing or the wrong shape at dispatch time.</summary>
public sealed class TemplateParseException(string message) : Exception(message);

/// <summary>
/// The frozen worker-side TemplatesService.parse/parseEmail: renders with
/// base-template inheritance and writes a parse-log row per attempt (success
/// or failure) — the ONLY writer of template_parse_logs. Log writes are
/// best-effort: a log failure never breaks the render.
/// </summary>
public sealed class TemplateParser(
    ITemplateStore store,
    ITemplateRenderer renderer,
    IAuthUnitOfWork unitOfWork)
{
    public sealed record EmailRender(string Subject, string Html, Guid TemplateId);

    /// <summary>
    /// Frozen parseEmail: the template must carry the email shape; the subject
    /// compiles first (fail fast), then the body renders through parse().
    /// </summary>
    public async Task<EmailRender> ParseEmailAsync(
        string identifier, JsonObject data, Guid? userId = null, CancellationToken ct = default)
    {
        var template = await store.FindByIdentifierAsync(identifier, withChildren: true, withBase: true, ct)
            ?? throw new TemplateParseException($"Template '{identifier}' not found");

        if (template.Email is null)
        {
            throw new TemplateParseException($"Template '{identifier}' is not an email template");
        }

        string subject;
        try
        {
            subject = renderer.Render(template.Email.Subject, baseContent: null, data);
        }
        catch (TemplateRenderException exception)
        {
            throw new TemplateParseException($"Invalid Handlebars template: {exception.Message}");
        }

        var html = await ParseAsync(template, data, userId, ct);
        return new EmailRender(subject, html, template.Id);
    }

    /// <summary>Frozen parse: schema validation, composition, and the parse-log write.</summary>
    public async Task<string> ParseAsync(
        Template template, JsonObject data, Guid? userId, CancellationToken ct = default)
    {
        var issues = TemplateSchema.Validate(JsonNode.Parse(template.SchemaJson), data);
        if (issues.Count > 0)
        {
            await LogAsync(template, data, userId, output: null,
                errorCode: TemplateParseErrorCodes.Schema,
                errorMessage: string.Join("; ", issues.Select(i => $"{i.Path}: expected {i.Expected}, got {i.Received}")),
                ct);
            throw new TemplateParseSchemaException(issues);
        }

        var baseContent = template.Category != TemplateCategories.Base
            ? template.BaseTemplate?.Content
            : null;

        string html;
        try
        {
            html = renderer.Render(template.Content, baseContent, data);
        }
        catch (TemplateRenderException exception)
        {
            await LogAsync(template, data, userId, output: null,
                errorCode: TemplateParseErrorCodes.HbsRuntime, errorMessage: exception.Message, ct);
            throw new TemplateParseException($"Invalid Handlebars template: {exception.Message}");
        }

        await LogAsync(template, data, userId, output: html, errorCode: null, errorMessage: null, ct);
        return html;
    }

    private async Task LogAsync(
        Template template,
        JsonObject data,
        Guid? userId,
        string? output,
        string? errorCode,
        string? errorMessage,
        CancellationToken ct)
    {
        try
        {
            await store.AddParseLogAsync(
                new TemplateParseLog
                {
                    Id = Guid.NewGuid(),
                    TemplateId = template.Id,
                    TemplateName = template.Name,
                    Category = template.Category,
                    InputJson = data.ToJsonString(),
                    OutputLength = output?.Length,
                    Success = errorCode is null,
                    ErrorCode = errorCode,
                    ErrorMessage = errorMessage,
                    UserId = userId,
                    TriggeredBy = TemplateParseTriggers.Internal,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Frozen behavior: parse-log persistence is best-effort.
        }
    }
}
