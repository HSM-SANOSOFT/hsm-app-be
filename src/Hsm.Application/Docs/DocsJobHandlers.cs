using System.Text.Json.Nodes;
using Hsm.Application.Ports;
using Hsm.Application.Templates;
using Hsm.Domain.Docs;

namespace Hsm.Application.Docs;

/// <summary>
/// The queued generation job (frozen worker docs-processor.service.ts), with
/// QuestPDF standing in for Puppeteer behind <see cref="IDocumentPdfRenderer"/>.
/// The observable persistence is the frozen flow exactly: PENDING →
/// PROCESSING → blob upload → one transaction (version max+1, storage object,
/// provenance, optional link + entity fields) → COMPLETED; on any failure the
/// uploaded blob is best-effort removed and the document row is marked FAILED
/// before the error re-throws into the queue's retry policy.
/// </summary>
public sealed class GenerateDocumentJobHandler(
    IDocumentStore store,
    ITemplateStore templateStore,
    TemplateParser parser,
    IDocumentPdfRenderer pdfRenderer,
    IObjectStorage storage,
    DocsOptions options)
{
    public async Task HandleAsync(GenerateDocumentJob job, CancellationToken ct = default)
    {
        await store.SetStatusAsync(job.DocumentId, DocumentStatuses.Processing, ct);

        string? uploadedKey = null;
        try
        {
            var template = await templateStore.FindByIdentifierAsync(
                    job.TemplateIdentifier, withChildren: true, withBase: true, ct)
                ?? throw new TemplateParseException($"Template '{job.TemplateIdentifier}' not found");
            if (!template.IsActive)
            {
                throw new TemplateParseException($"Template '{template.Name}' is not active");
            }

            if (template.Doc is null)
            {
                throw new TemplateParseException(
                    $"Template '{job.TemplateIdentifier}' is not a DOCS category template");
            }

            // Frozen formats: PDF via the renderer port; EXCEL/WORD are not
            // carried into the minor (see the U15 report) — they fail the job
            // exactly as the frozen WORD path did ("Unsupported document
            // format"), leaving the document FAILED.
            if (template.Doc.Format != "PDF")
            {
                throw new TemplateParseException($"Unsupported document format: {template.Doc.Format}");
            }

            var data = JsonNode.Parse(job.DataJson) as JsonObject ?? [];
            var html = await parser.ParseAsync(template, data, userId: null, ct);
            // The synchronous QuestPDF render runs off the consumer's async
            // flow so a long layout cannot stall queue throughput.
            var pdf = await Task.Run(() => pdfRenderer.Render(html), ct);

            var fileId = Guid.NewGuid();
            var key = StorageKeys.MakeKey(template.Doc.DocumentCode, fileId.ToString());
            var filename = $"{template.Doc.DocumentCode}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.pdf";
            using (var content = new MemoryStream(pdf))
            {
                await storage.PutAsync(key, content, "application/pdf", options.Bucket, ct);
            }

            uploadedKey = key;
            await store.AddGeneratedVersionAsync(
                new GeneratedVersionRecord(
                    job.DocumentId,
                    filename,
                    "application/pdf",
                    pdf.Length,
                    fileId,
                    key,
                    options.Bucket,
                    template.Name,
                    job.DataJson,
                    job.EntityId,
                    job.EntityType),
                ct);
            await store.SetStatusAsync(job.DocumentId, DocumentStatuses.Completed, ct);
        }
        catch
        {
            // Frozen cleanup: remove the orphaned blob if the transaction
            // failed after upload, then FAILED — each guarded so a cleanup
            // failure never masks the original error.
            if (uploadedKey is not null)
            {
                try
                {
                    await storage.DeleteAsync(uploadedKey, options.Bucket, CancellationToken.None);
                }
                catch (ObjectStorageException)
                {
                    // Logged by the processor via the re-thrown original.
                }
            }

            try
            {
                await store.SetStatusAsync(job.DocumentId, DocumentStatuses.Failed, CancellationToken.None);
            }
            catch (Exception)
            {
                // A status-write outage must not mask the original error.
            }

            throw;
        }
    }
}
