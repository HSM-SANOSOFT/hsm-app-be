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
/// Job ids are reserved ahead of enqueue so a caller can persist the id in
/// the same transaction as the document row it identifies, then enqueue
/// after commit — see <see cref="Hsm.Application.Docs.Commands.GenerateDocument.GenerateDocumentHandler"/>.
/// </summary>
public interface IDocsJobDispatcher
{
    /// <summary>Mints the id the next generate-document enqueue will carry, without enqueuing.</summary>
    string ReserveGenerateDocumentJobId();

    Task EnqueueGenerateDocumentAsync(string jobId, GenerateDocumentJob job, CancellationToken ct = default);
}
