namespace Hsm.Application.Docs;

/// <summary>
/// Documents blob-store options. The frozen stack hardcoded the single
/// bucket 'hsm-docs' (packages/storage/src/s3/s3.buckets.ts); here the same
/// default is configuration (Docs:Bucket).
/// </summary>
public sealed class DocsOptions
{
    public string Bucket { get; init; } = "hsm-docs";
}
