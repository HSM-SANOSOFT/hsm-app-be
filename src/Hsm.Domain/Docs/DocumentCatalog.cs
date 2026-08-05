namespace Hsm.Domain.Docs;

/// <summary>Document type values.</summary>
public static class DocumentTypes
{
    public const string Generated = "GENERATED";
    public const string Uploaded = "UPLOADED";

    public static readonly IReadOnlyList<string> All = [Generated, Uploaded];
}

/// <summary>Document status values.</summary>
public static class DocumentStatuses
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";

    public static readonly IReadOnlyList<string> All = [Pending, Processing, Completed, Failed];
}

/// <summary>Document source values.</summary>
public static class DocumentSources
{
    public const string Template = "TEMPLATE";
    public const string Manual = "MANUAL";
}
