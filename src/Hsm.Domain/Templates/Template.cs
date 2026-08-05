namespace Hsm.Domain.Templates;

/// <summary>
/// A stored template. One parent row plus at most one category-matching
/// child row (email / sms / doc) sharing the parent's primary key — three
/// shapes, not single-table inheritance. The CHECK constraint (BASE has no
/// base reference, everything else requires one) is enforced by the
/// create/update handlers as a domain invariant.
/// </summary>
public class Template
{
    public Guid Id { get; set; }

    /// <summary>One of <see cref="TemplateCategories"/>. Immutable after create.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Globally unique template name.</summary>
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>The mini-schema (jsonb) describing the expected render data.</summary>
    public string SchemaJson { get; set; } = "{}";

    /// <summary>Handlebars source.</summary>
    public string Content { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Self-reference to the BASE template (null for BASE category).</summary>
    public Guid? BaseTemplateId { get; set; }

    public Template? BaseTemplate { get; set; }

    public TemplateEmail? Email { get; set; }

    public TemplateSms? Sms { get; set; }

    public TemplateDoc? Doc { get; set; }
}

/// <summary>Email shape: shares the parent's PK.</summary>
public class TemplateEmail
{
    public Guid Id { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string FromEmail { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    public List<string>? Cc { get; set; }

    public List<string>? Bcc { get; set; }

    public bool HasAttachment { get; set; }
}

/// <summary>SMS shape: shares the parent's PK.</summary>
public class TemplateSms
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string TemplateName { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;
}

/// <summary>Document shape: shares the parent's PK.</summary>
public class TemplateDoc
{
    public Guid Id { get; set; }

    public string DocumentCode { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Size { get; set; } = string.Empty;

    public string Orientation { get; set; } = string.Empty;
}

/// <summary>
/// One render attempt: success or failure, with the input data and
/// denormalized template name/category so the log survives template
/// deletion (the FK nulls out, the row stays).
/// </summary>
public class TemplateParseLog
{
    public Guid Id { get; set; }

    public Guid? TemplateId { get; set; }

    public string TemplateName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string InputJson { get; set; } = "{}";

    public int? OutputLength { get; set; }

    public bool Success { get; set; }

    /// <summary>One of <see cref="TemplateParseErrorCodes"/>, null on success.</summary>
    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>One of <see cref="TemplateParseTriggers"/>.</summary>
    public string TriggeredBy { get; set; } = TemplateParseTriggers.Internal;

    public DateTimeOffset CreatedAt { get; set; }
}
