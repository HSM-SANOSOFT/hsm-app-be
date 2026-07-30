using System.Text.Json.Nodes;

namespace Hsm.Application.Templates;

/// <summary>The create/update payload as seen by the handlers (frozen CreateTemplatePayloadDto).</summary>
public sealed record TemplatePayload(
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
