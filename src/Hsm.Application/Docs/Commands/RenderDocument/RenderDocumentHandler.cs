using System.Text.Json.Nodes;
using Hsm.Application.Abstractions;
using Hsm.Application.Ports;
using Hsm.Application.Templates;
using Hsm.Domain.Docs;

namespace Hsm.Application.Docs.Commands.RenderDocument;

/// <summary>
/// The queued generation job (frozen worker docs-processor.service.ts), with
/// QuestPDF standing in for Puppeteer behind <see cref="IDocumentPdfRenderer"/>.
/// The observable persistence is the frozen flow exactly: PENDING →
/// PROCESSING → blob upload → one transaction (version max+1, storage object,
/// provenance, optional link + entity fields) → COMPLETED; on any failure the
/// uploaded blob is best-effort removed and the document row is marked FAILED
/// before the error re-throws into the queue's retry policy.
///
/// <para>Dispatched through <see cref="IDispatcher"/> — telemetry,
/// authorization against the enqueuing actor, validation — but NOT under a
/// pipeline-owned transaction: the command carries
/// <see cref="NoAmbientTransactionAttribute"/>, because the FAILED write below
/// (and the template parse-log row <c>TemplateParser</c> writes on a schema
/// failure) must survive the very re-throw that reports the attempt, and
/// because a transaction has no business staying open across a PDF render and
/// an S3 upload. Each store call commits on its own, as it did before this
/// command was routed through a dispatcher.</para>
/// </summary>
public sealed class RenderDocumentHandler(
    IDocumentStore store,
    ITemplateStore templateStore,
    TemplateParser parser,
    IDocumentPdfRenderer pdfRenderer,
    IObjectStorage storage,
    DocsOptions options) : IRequestHandler<RenderDocumentCommand, Unit>
{
    public async Task<Unit> HandleAsync(RenderDocumentCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await store.SetStatusAsync(request.DocumentId, DocumentStatuses.Processing, ct);

        string? uploadedKey = null;
        try
        {
            var template = await templateStore.FindByIdentifierAsync(
                    request.TemplateIdentifier, withChildren: true, withBase: true, ct)
                ?? throw new TemplateParseException($"Template '{request.TemplateIdentifier}' not found");
            if (!template.IsActive)
            {
                throw new TemplateParseException($"Template '{template.Name}' is not active");
            }

            if (template.Doc is null)
            {
                throw new TemplateParseException(
                    $"Template '{request.TemplateIdentifier}' is not a DOCS category template");
            }

            // Frozen formats: PDF via the renderer port; EXCEL/WORD are not
            // carried into the minor (see the U15 report) — they fail the job
            // exactly as the frozen WORD path did ("Unsupported document
            // format"), leaving the document FAILED.
            if (template.Doc.Format != "PDF")
            {
                throw new TemplateParseException($"Unsupported document format: {template.Doc.Format}");
            }

            var data = JsonNode.Parse(request.DataJson) as JsonObject ?? [];
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
                    request.DocumentId,
                    filename,
                    "application/pdf",
                    pdf.Length,
                    fileId,
                    key,
                    options.Bucket,
                    template.Name,
                    request.DataJson,
                    request.EntityId,
                    request.EntityType),
                ct);
            await store.SetStatusAsync(request.DocumentId, DocumentStatuses.Completed, ct);
        }
        catch
        {
            // Frozen cleanup: remove the orphaned blob if the work failed
            // after upload, then FAILED — each guarded so a cleanup failure
            // never masks the original error.
            if (uploadedKey is not null)
            {
                try
                {
                    await storage.DeleteAsync(uploadedKey, options.Bucket, CancellationToken.None);
                }
                catch (ObjectStorageException)
                {
                    // Logged by the consumer via the re-thrown original.
                }
            }

            try
            {
                await store.SetStatusAsync(request.DocumentId, DocumentStatuses.Failed, CancellationToken.None);
            }
            catch (Exception)
            {
                // A status-write outage must not mask the original error.
            }

            throw;
        }

        return Unit.Value;
    }
}
