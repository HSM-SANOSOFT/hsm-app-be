namespace Hsm.Application.Docs;

/// <summary>The queued generation job (frozen GenerateDocumentJobPayloadDto).</summary>
public sealed record GenerateDocumentJob(
    Guid DocumentId,
    string TemplateIdentifier,
    string DataJson,
    string? EntityId,
    string? EntityType);

/// <summary>
/// Hand-off port for document generation (frozen BullMQ 'document' queue).
/// Returns the job id the frozen API echoed to clients.
/// </summary>
public interface IDocsJobDispatcher
{
    Task<string> EnqueueGenerateDocumentAsync(GenerateDocumentJob job, CancellationToken ct = default);
}
