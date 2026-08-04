using System.Text.Json;
using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Docs;
using Hsm.Application.Docs.Commands.DeleteDocument;
using Hsm.Application.Docs.Commands.DeleteDocumentBlobs;
using Hsm.Application.Docs.Commands.GenerateDocument;
using Hsm.Application.Docs.Commands.RenderDocument;
using Hsm.Application.Docs.Commands.UploadDocuments;
using Hsm.Application.Docs.Queries.GetDocument;
using Hsm.Application.Docs.Queries.GetDocumentUrl;
using Hsm.Application.Docs.Queries.ListDocuments;
using Hsm.Application.Docs.Queries.PresignDocuments;
using Hsm.Application.Errors;
using Hsm.Application.Ports;
using Hsm.Contracts;

namespace Hsm.Api.Documents;

/// <summary>
/// The documents resource. Every delegate does transport work only — bind,
/// dispatch, project, choose a status. There is no authentication call and no
/// role check here — the actor is installed by middleware and the policy
/// rides on the request type (every Docs command/query below is
/// authenticated-only, no role restriction). The one exception is
/// <see cref="ListDocuments"/>, which reads <see cref="ICurrentPrincipal"/>
/// directly: <see cref="DocumentListFilter"/> carries its scoping id baked in
/// by the caller (ListDocumentsQuery's own doc comment), unlike Get/Delete/
/// Upload/GenerateDocument, whose handlers each self-scope from
/// ICurrentPrincipal internally.
/// </summary>
public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var documents = app.MapGroup("/api/v1/documents").WithTags("Documents");

        documents.MapGet("/", ListDocuments)
            .WithSummary("List the caller's documents, newest first.")
            .Produces<PagedResult<DocumentResource>>();

        documents.MapPost("/", UploadDocuments)
            .WithSummary("Upload one or more documents (multipart/form-data, field 'files').")
            .Produces<UploadResultResource>(StatusCodes.Status201Created);

        documents.MapPost("/generated", GenerateDocument)
            .WithSummary("Generate a document from a template.")
            .Produces<AcceptedDocumentResponse>(StatusCodes.Status202Accepted);

        documents.MapPost("/urls", PresignDocuments)
            .WithSummary("Presign GET URLs for a batch of stored objects.")
            .Produces<IReadOnlyList<PresignedItemResource>>();

        documents.MapGet("/{id:guid}", GetDocument)
            .WithSummary("Read one document with its versions.")
            .Produces<DocumentDetailResource>();

        documents.MapDelete("/{id:guid}", DeleteDocument)
            .WithSummary("Delete one document.")
            .Produces(StatusCodes.Status204NoContent);

        documents.MapDelete("/", DeleteDocumentsBulk)
            .WithSummary("Delete a batch of documents (?ids=a,b,c).")
            .Produces(StatusCodes.Status204NoContent);

        documents.MapGet("/{id:guid}/url", GetDocumentUrl)
            .WithSummary("Presign a GET URL for a document's latest version.")
            .Produces<DocumentUrlResource>();
    }

    private static async Task<IResult> ListDocuments(
        IDispatcher dispatcher,
        ICurrentPrincipal principal,
        CancellationToken ct,
        string? entityId = null,
        string? entityType = null,
        string? type = null,
        string? status = null,
        int page = PagingRules.DefaultPage,
        int pageSize = PagingRules.DefaultPageSize)
    {
        var actor = principal.Actor ?? throw new UnauthorizedException();
        var filter = new DocumentListFilter(Guid.Parse(actor.Id), entityId, entityType, type, status);
        var result = await dispatcher.Send(new ListDocumentsQuery(filter, page, pageSize), ct);
        return Results.Ok(result.Map(DocumentResource.From));
    }

    private static async Task<IResult> GenerateDocument(
        GenerateDocumentRequest request, IDispatcher dispatcher, IJobQueue queue, CancellationToken ct)
    {
        var dataJson = request.Data?.ToJsonString() ?? "{}";
        var result = await dispatcher.Send(
            new GenerateDocumentCommand(
                request.TemplateIdentifier, dataJson, request.Title, request.Description,
                request.EntityId, request.EntityType),
            ct);

        // Enqueued HERE, after dispatch returns — unlike Task 6's
        // ResendEmailRecipientHandler, GenerateDocumentHandler does NOT
        // enqueue itself (its own doc comment: TransactionBehavior commits
        // the document row only once HandleAsync returns, so enqueuing any
        // earlier would race that commit). CancellationToken.None,
        // deliberately: this is post-commit work — the document row already
        // exists — and must not be abandoned just because the client hung up
        // between the commit and this call.
        await queue.EnqueueAsync(
            new RenderDocumentCommand(
                result.DocumentId, request.TemplateIdentifier, dataJson, request.EntityId, request.EntityType),
            CancellationToken.None);

        return Results.Accepted(
            $"/api/v1/documents/{result.DocumentId}",
            new AcceptedDocumentResponse(result.DocumentId, result.JobId));
    }

    private static async Task<IResult> GetDocument(Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        Results.Ok(DocumentDetailResource.From(await dispatcher.Send(new GetDocumentQuery(id), ct)));

    private static async Task<IResult> DeleteDocument(
        Guid id, IDispatcher dispatcher, IJobQueue queue, CancellationToken ct)
    {
        var result = await dispatcher.Send(new DeleteDocumentCommand(id), ct);

        // Enqueued HERE, after dispatch returns — TransactionBehavior has
        // already committed the soft-delete by now. DeleteDocumentHandler
        // deliberately does not delete the blobs itself (see its own doc
        // comment): object-store deletes are not transactional, so they must
        // never run until the row that "owns" them is durably gone.
        await EnqueueBlobCleanupAsync(queue, result);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteDocumentsBulk(
        string? ids, IDispatcher dispatcher, IUnitOfWork unitOfWork, IJobQueue queue, CancellationToken ct)
    {
        var documentIds = ParseIds(ids);

        // One ambient transaction for the whole batch: DeleteDocumentCommand
        // dispatches through TransactionBehavior, and EfUnitOfWork.
        // ExecuteInTransactionAsync only opens a REAL transaction when none is
        // already open — every Send below finds this one already ambient and
        // joins it instead (see EfUnitOfWork's own doc comment). An unknown id
        // partway through the batch throws NotFoundException, which unwinds
        // out of this delegate without a commit ever happening, so the whole
        // batch rolls back rather than applying partially.
        var results = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var deleted = new List<DeleteDocumentResult>(documentIds.Count);
                foreach (var id in documentIds)
                {
                    deleted.Add(await dispatcher.Send(new DeleteDocumentCommand(id), token));
                }

                return deleted;
            },
            ct);

        // Enqueued HERE, once the WHOLE batch's transaction has committed —
        // not per-id inside the loop above. Blob deletes are not
        // transactional: enqueuing (or running) one before every id in the
        // batch is confirmed durable would mean an id that survives a
        // later-id rollback could still lose its blob, exactly the bug this
        // fix closes.
        foreach (var result in results)
        {
            await EnqueueBlobCleanupAsync(queue, result);
        }

        return Results.NoContent();
    }

    private static Task EnqueueBlobCleanupAsync(IJobQueue queue, DeleteDocumentResult result) =>
        result.Blobs.Count > 0
            // CancellationToken.None, deliberately: post-commit cleanup of
            // objects belonging to a row that is now durably gone — must not
            // be abandoned just because the client hung up.
            ? queue.EnqueueAsync(
                new DeleteDocumentBlobsCommand(result.DocumentId, result.Blobs), CancellationToken.None)
            : Task.CompletedTask;

    private static async Task<IResult> PresignDocuments(
        PresignRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        // Built BEFORE dispatcher.Send, so a missing/null nested array must
        // degrade to empty rather than NRE — otherwise a body-shape mistake
        // would surface as a bare 500 ahead of AuthorizationBehavior ever
        // getting a chance to answer 401/pipeline-side 400 validation.
        var items = (request.Items ?? [])
            .Select(item => new PresignItem(
                item.Bucket, [.. (item.Files ?? []).Select(f => new PresignFileRef(f.FolderName, f.FileId))]))
            .ToList();
        var results = await dispatcher.Send(
            new PresignDocumentsQuery(items, request.ContentDisposition, request.ExpiresInSeconds), ct);
        return Results.Ok(results.Select(ToPresignedResource).ToList());
    }

    private static async Task<IResult> GetDocumentUrl(Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        Results.Ok(new DocumentUrlResource(await dispatcher.Send(new GetDocumentUrlQuery(id), ct)));

    private static async Task<IResult> UploadDocuments(HttpContext ctx, IDispatcher dispatcher, CancellationToken ct)
    {
        var form = await ctx.Request.ReadFormAsync(ct);

        // Frozen FilesInterceptor('files') / Task 2's sweep: a file under any
        // other field name is a field validation failure, not a silently
        // ignored file.
        var unexpectedField = form.Files.FirstOrDefault(f => f.Name != "files")?.Name;
        if (unexpectedField is not null)
        {
            throw new ValidationException(
                [
                    new FluentValidation.Results.ValidationFailure(
                        "files", $"Unexpected multipart field '{unexpectedField}'; expected 'files'."),
                ]);
        }

        var payload = ReadUploadPayload(form);
        var entityId = form.TryGetValue("entityId", out var entityIdValues) ? entityIdValues[^1] : null;
        var entityType = form.TryGetValue("entityType", out var entityTypeValues) ? entityTypeValues[^1] : null;

        // Stream-through: OpenReadStream() straight into the command, never a
        // MemoryStream+ToArray copy — a 30 MB upload would otherwise put
        // three copies of it on the heap.
        var files = form.Files.GetFiles("files")
            .Select(file => new UploadFileUpload(file.FileName, file.ContentType, file.Length, file.OpenReadStream()))
            .ToList();

        var result = await dispatcher.Send(new UploadDocumentsCommand(payload, entityId, entityType, files), ct);

        var items = result.S3Result
            .Select(item => new UploadedItemResource(
                item.Bucket,
                [.. item.Files.Select(f => new UploadedFileResource(f.FileId, f.Filename, f.Key))]))
            .ToList();
        // No single Location: one call can create several documents.
        return Results.Created((string?)null, new UploadResultResource(items, result.DocumentIds));
    }

    private static PresignedItemResource ToPresignedResource(PresignedItem item) =>
        new(item.Bucket, [.. item.Files.Select(f => new PresignedFileResource(f.FileId, f.Key, f.Url))]);

    /// <summary>
    /// <c>?ids=a,b,c</c>: comma-separated GUIDs, not a DELETE body — poorly
    /// supported by intermediaries and by every HTTP client. A missing or
    /// malformed entry is a field validation failure on <c>ids</c>.
    /// </summary>
    private static List<Guid> ParseIds(string? ids)
    {
        var parts = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ValidationException(
                [new FluentValidation.Results.ValidationFailure("ids", "At least one id is required.")]);
        }

        var guids = new List<Guid>(parts.Length);
        foreach (var part in parts)
        {
            if (!Guid.TryParse(part, out var guid))
            {
                throw new ValidationException(
                    [new FluentValidation.Results.ValidationFailure("ids", $"'{part}' is not a valid id.")]);
            }

            guids.Add(guid);
        }

        return guids;
    }

    /// <summary>
    /// The multipart "payload" field is a JSON string matching
    /// <see cref="UploadDocumentsCommand"/>'s own payload shape
    /// (<c>UploadPayloadItem</c>/<c>UploadPayloadFile</c>) —
    /// <c>[{"bucket":"...","files":[{"folderName":"...","fileName":"..."}]}]</c>
    /// — deserialized straight into those types rather than traversed by hand
    /// through an intermediate JsonObject shape: this IS the reshape the old
    /// DocsEndpoints comment deferred to this task. An unparseable value
    /// surfaces as a bare JsonException, which HsmExceptionHandler has no arm
    /// for and answers with a plain 500 — deliberately, matching
    /// HsmExceptionHandler's own rule that a body a real client never
    /// hand-writes is our bug to fix, not the caller's malformed request.
    /// </summary>
    private static List<UploadPayloadItem> ReadUploadPayload(IFormCollection form)
    {
        if (!form.TryGetValue("payload", out var payloadValues) || string.IsNullOrWhiteSpace(payloadValues[^1]))
        {
            return [];
        }

        var items = JsonSerializer.Deserialize<List<UploadPayloadItem>>(payloadValues[^1]!, JsonSerializerOptions.Web)
            ?? [];
        ValidateUploadPayloadShape(items);
        return items;
    }

    /// <summary>
    /// A well-formed JSON array with a missing/null required sub-field (no
    /// "bucket", no "files", or a file entry missing "folderName"/"fileName")
    /// is a field validation failure on "payload" — not a silent degrade to a
    /// default value, and not the <see cref="NullReferenceException"/>
    /// <c>UploadDocumentsHandler</c>'s own matching loop would otherwise throw
    /// two layers down (a bare 500, outside the closed exception set). This is
    /// the field-keyed-400 half of the reshape the old DocsEndpoints comment
    /// deferred to this task; unparseable JSON itself is a separate,
    /// deliberately-unchanged case — see <see cref="ReadUploadPayload"/>'s doc
    /// comment.
    /// </summary>
    private static void ValidateUploadPayloadShape(List<UploadPayloadItem> items)
    {
        foreach (var item in items)
        {
            if (item is null || item.Bucket is null || item.Files is null)
            {
                throw PayloadShapeFailure();
            }

            foreach (var file in item.Files)
            {
                if (file is null || file.FolderName is null || file.FileName is null)
                {
                    throw PayloadShapeFailure();
                }
            }
        }
    }

    private static ValidationException PayloadShapeFailure() =>
        new(
            [
                new FluentValidation.Results.ValidationFailure(
                    "payload",
                    "Each payload item requires 'bucket' and 'files' (each with 'folderName' and 'fileName')."),
            ]);
}
