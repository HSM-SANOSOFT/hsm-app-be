using System.Text.Json.Nodes;
using Hsm.Application.Templates;
using Hsm.Domain.Templates;

namespace Hsm.Api.Templates;

/// <summary>
/// The wire shape of a stored template (list view). <see cref="Identifier"/>
/// and <see cref="Name"/> both come from <see cref="Template.Name"/> — the
/// domain has exactly one human-authored, uniquely-indexed string column
/// (frozen templates.entity.ts: <c>@Unique(['name'])</c>, no separate slug
/// column), and it is that same value <c>GetTemplateQuery</c> matches on the
/// GET route's unconstrained <c>{id}</c> (see <see cref="TemplateEndpoints"/>'s
/// own doc comment on the asymmetric identifier form). Exposing it under both
/// names says explicitly "this is what you can fetch me by" without forcing a
/// client to already know that Name doubles as the lookup key.
///
/// <para><see cref="Template"/> carries no created/updated timestamps: the
/// frozen entity never had them (unlike <c>Document</c> or <c>EmailBatch</c>,
/// which do — see their own entities), and <c>TemplateStore</c> stamps none.
/// There is no data to report, so this resource does not claim any.</para>
/// </summary>
public sealed record TemplateResource(
    Guid Id, string Identifier, string Name, string Category, string? Description, bool IsActive)
{
    public static TemplateResource From(Template template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new TemplateResource(
            template.Id, template.Name, template.Name, template.Category, template.Description, template.IsActive);
    }
}

/// <summary>
/// The single-template read. Adds the template's actual definition —
/// <see cref="Schema"/> and <see cref="Content"/>, the mini-schema and
/// Handlebars source every create/update call writes — dropped from the list
/// view to keep it lean, but the entire reason a caller reads one template by
/// id. Also adds each category's real metadata shape: at most one of
/// <see cref="Email"/>/<see cref="Doc"/>/<see cref="Sms"/> is non-null,
/// matching <see cref="Template"/>'s "one parent + at most one child row"
/// shape. <see cref="BaseTemplateId"/> is the same round-trip argument as
/// <see cref="Sms"/>: create/update accept it
/// (<see cref="TemplatePayload.BaseTemplateId"/>) and every non-BASE category
/// requires it, so a caller needs it back to know which base a template
/// currently wraps —
/// <see cref="Hsm.Application.Templates.Queries.GetTemplate.GetTemplateHandler"/>
/// already loads <c>withBase: true</c>, so the join is free to expose.
/// </summary>
public sealed record TemplateDetailResource(
    Guid Id, string Identifier, string Name, string Category, string? Description, bool IsActive,
    JsonNode? Schema, string Content, Guid? BaseTemplateId,
    TemplateEmailResource? Email, TemplateDocResource? Doc, TemplateSmsResource? Sms)
{
    public static TemplateDetailResource From(Template template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var summary = TemplateResource.From(template);
        return new TemplateDetailResource(
            summary.Id, summary.Identifier, summary.Name, summary.Category, summary.Description, summary.IsActive,
            JsonNode.Parse(template.SchemaJson), template.Content, template.BaseTemplateId,
            template.Email is null ? null : TemplateEmailResource.From(template.Email),
            template.Doc is null ? null : TemplateDocResource.From(template.Doc),
            template.Sms is null ? null : TemplateSmsResource.FromSms(template.Sms));
    }
}

/// <summary>
/// The EMAIL_INTERNAL/EMAIL_EXTERNAL shape (frozen <c>template_coms_email</c> /
/// <see cref="TemplateEmail"/>) — a field-for-field mirror of the stored child
/// row. Deliberately not <c>(Subject, Body, Attachments)</c>: that shape
/// (an earlier plan draft) matches no column here — the row has no body text
/// or attachment-list, only a <see cref="HasAttachment"/> flag; the actual
/// body is the parent template's Handlebars <see cref="TemplateDetailResource.Content"/>.
/// </summary>
public sealed record TemplateEmailResource(
    string Subject, string FromEmail, string FromName,
    IReadOnlyList<string>? Cc, IReadOnlyList<string>? Bcc, bool HasAttachment)
{
    public static TemplateEmailResource From(TemplateEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return new TemplateEmailResource(
            email.Subject, email.FromEmail, email.FromName, email.Cc, email.Bcc, email.HasAttachment);
    }
}

/// <summary>
/// The DOCS shape (frozen <c>template_docs</c> / <see cref="TemplateDoc"/>).
/// Deliberately not <c>(Body, Header, Footer)</c>: that shape (an earlier plan
/// draft) matches no column here — the four stored fields are
/// DocumentCodesEnum-family selectors that pick a document layout, not
/// free-form content; the actual content is, again, the parent template's
/// Handlebars <see cref="TemplateDetailResource.Content"/>.
/// </summary>
public sealed record TemplateDocResource(string DocumentCode, string Format, string Size, string Orientation)
{
    public static TemplateDocResource From(TemplateDoc doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return new TemplateDocResource(doc.DocumentCode, doc.Format, doc.Size, doc.Orientation);
    }
}

/// <summary>
/// The SMS_INTERNAL/SMS_EXTERNAL shape (frozen <c>template_coms_sms</c> /
/// <see cref="TemplateSms"/>). Not in the plan's original resource list at
/// all (only Email/Doc were drafted there) — added so an SMS-category
/// template round-trips: Create/Update already accept an
/// <see cref="SmsShape"/> block and persist it, and without a matching read
/// shape a caller could write SMS metadata but never see it again.
/// </summary>
public sealed record TemplateSmsResource(string Provider, string TemplateName, string From)
{
    // Named FromSms, not From: a positional record property is also named
    // From (the SMS sender id) and CS0102 refuses the resulting name clash
    // with a same-named static method.
    public static TemplateSmsResource FromSms(TemplateSms sms)
    {
        ArgumentNullException.ThrowIfNull(sms);
        return new TemplateSmsResource(sms.Provider, sms.TemplateName, sms.From);
    }
}

public sealed record ValidateTemplateRequest(string Identifier, JsonNode? Data);

public sealed record ValidateTemplateResource(bool Valid, Guid? TemplateId, IReadOnlyList<TemplateIssueResource> Issues);

/// <summary>
/// One schema-validation finding. <see cref="Message"/> is the
/// "expected X, got Y" format already used at every other
/// <see cref="TemplateSchemaIssue"/> call site (<c>SendEmailHandler</c>,
/// <c>TemplateParser</c>) — collapsed from the query result's three-field
/// <see cref="TemplateSchemaIssue"/> (Path/Expected/Received) using that
/// existing phrasing rather than inventing a new one.
/// </summary>
public sealed record TemplateIssueResource(string Path, string Message)
{
    public static TemplateIssueResource From(TemplateSchemaIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new TemplateIssueResource(issue.Path, $"expected {issue.Expected}, got {issue.Received}");
    }
}

public sealed record DraftRenderRequest(string Content, string? BaseTemplateId, JsonObject? SampleData);

public sealed record DraftRenderResource(string Html);
