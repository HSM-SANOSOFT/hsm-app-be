namespace Hsm.Domain.Docs;

/// <summary>Frozen DocumentTypeEnum values (packages/common/src/enums/docs.enum.ts).</summary>
public static class DocumentTypes
{
    public const string Generated = "GENERATED";
    public const string Uploaded = "UPLOADED";

    public static readonly IReadOnlyList<string> All = [Generated, Uploaded];
}

/// <summary>Frozen DocumentStatusEnum values.</summary>
public static class DocumentStatuses
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";

    public static readonly IReadOnlyList<string> All = [Pending, Processing, Completed, Failed];
}

/// <summary>Frozen DocumentSourceEnum values.</summary>
public static class DocumentSources
{
    public const string Template = "TEMPLATE";
    public const string Manual = "MANUAL";
}
