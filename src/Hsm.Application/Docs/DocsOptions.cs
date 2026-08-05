namespace Hsm.Application.Docs;

/// <summary>
/// Documents blob-store options. Configurable via Docs:Bucket; defaults to
/// the single bucket 'hsm-docs'.
/// </summary>
public sealed class DocsOptions
{
    public string Bucket { get; init; } = "hsm-docs";
}
