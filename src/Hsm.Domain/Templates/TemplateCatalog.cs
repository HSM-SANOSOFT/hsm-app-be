namespace Hsm.Domain.Templates;

/// <summary>
/// Template categories frozen at packages/common/src/enums/templates.enum.ts.
/// Values are contract.
/// </summary>
public static class TemplateCategories
{
    public const string Base = "BASE";
    public const string EmailInternal = "EMAIL_INTERNAL";
    public const string EmailExternal = "EMAIL_EXTERNAL";
    public const string Docs = "DOCS";
    public const string SmsInternal = "SMS_INTERNAL";
    public const string SmsExternal = "SMS_EXTERNAL";

    public static readonly IReadOnlyList<string> All =
        [Base, EmailInternal, EmailExternal, Docs, SmsInternal, SmsExternal];

    public static bool IsEmail(string category) => category is EmailInternal or EmailExternal;

    public static bool IsSms(string category) => category is SmsInternal or SmsExternal;
}

/// <summary>
/// Category-membership check shared by the template validators (Task 3):
/// <see cref="TemplateCategories.All"/> stated once so "is this a template
/// category" cannot drift between <c>CreateTemplateValidator</c> and
/// <c>UpdateTemplateValidator</c>.
/// </summary>
public static class TemplateCatalog
{
    public static bool IsKnownCategory(string? category) =>
        category is not null && TemplateCategories.All.Contains(category, StringComparer.Ordinal);
}

/// <summary>Frozen TemplateParseTriggerEnum values.</summary>
public static class TemplateParseTriggers
{
    public const string Http = "HTTP";
    public const string Internal = "INTERNAL";
}

/// <summary>Frozen TemplateParseErrorCodeEnum values.</summary>
public static class TemplateParseErrorCodes
{
    public const string Schema = "SCHEMA";
    public const string HbsCompile = "HBS_COMPILE";
    public const string HbsRuntime = "HBS_RUNTIME";
    public const string NotFound = "NOT_FOUND";
    public const string Unknown = "UNKNOWN";
}

/// <summary>Frozen document enums (docs.enum.ts) used by the DOCS shape.</summary>
public static class DocumentFormats
{
    public static readonly IReadOnlyList<string> All = ["PDF", "WORD", "EXCEL"];
}

/// <summary>Frozen document page sizes.</summary>
public static class DocumentSizes
{
    public static readonly IReadOnlyList<string> All = ["A4", "A3", "LETTER"];
}

/// <summary>Frozen document orientations.</summary>
public static class DocumentOrientations
{
    public static readonly IReadOnlyList<string> All = ["PORTRAIT", "LANDSCAPE"];
}

/// <summary>
/// The frozen DocumentCodesEnum (57 institutional document codes). Values are
/// contract for the DOCS template shape.
/// </summary>
public static class DocumentCodes
{
    public static readonly IReadOnlyList<string> All =
    [
        "HCU-001", "HCU-002", "HCU-003", "HCU-005", "HCU-006", "HCU-007", "HCU-008",
        "HCU-010-A", "HCU-010-B", "HCU-012-A", "HCU-012-B", "HCU-013-A", "HCU-013-B",
        "HCU-016", "HCU-017", "HCU-018", "HCU-018-A", "HCU-019", "HCU-020", "HCU-022",
        "HCU-024", "HCU-028", "HCU-028-A", "HCU-028-A-1", "HCU-028-B", "HCU-028-B-1",
        "HCU-028-B-2", "HCU-028-C", "HCU-028-C-1", "HCU-028-C-2", "HCU-033", "HCU-038",
        "HCU-051", "HCU-052", "HCU-053", "HCU-054", "HCU-055", "HCU-056", "HCU-056-A",
        "HCU-056-B", "HCU-057", "HCU-057-A", "HCU-058", "HCU-058-A", "HCU-060",
        "HCU-113", "HCU-114", "HCU-115", "HCU-116", "HCU-117", "HCU-118", "HCU-119",
        "HCU-120", "HCU-121", "HCU-122", "ANEXO-1", "ANEXO-2",
    ];
}
