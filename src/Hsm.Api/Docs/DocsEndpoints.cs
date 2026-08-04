using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using Hsm.Api.Auth;
using Hsm.Api.Http;
using Hsm.Application;
using Hsm.Application.Abstractions;
using Hsm.Application.Docs;
using Hsm.Application.Docs.Commands.DeleteDocument;
using Hsm.Application.Docs.Commands.GenerateDocument;
using Hsm.Application.Docs.Commands.RenderDocument;
using Hsm.Application.Docs.Commands.UploadDocuments;
using Hsm.Application.Docs.Queries.GetDocument;
using Hsm.Application.Docs.Queries.GetDocumentUrl;
using Hsm.Application.Docs.Queries.ListDocuments;
using Hsm.Application.Docs.Queries.PresignDocuments;
using Hsm.Application.Ports;
using Hsm.Domain.Docs;

namespace Hsm.Api.Docs;

/// <summary>
/// The nine frozen /v1/docs operations (docs.controller.ts). Every route is
/// @Roles() with no arguments — any authenticated, onboarded user. POST
/// routes return 201 (the frozen runtime default; the OpenAPI snapshot
/// under-documented these as 200). Two frozen stubs are pinned as stubs:
/// POST /create and the bulk DELETE answered the bare success envelope
/// without ever touching storage. Nested-payload shape validation (the
/// bucket/files traversals below) is a full reshape left to Task 7 — Task 3
/// only ports the business rules in Step 6's table (files non-empty, presign
/// expiry bounds); a malformed nested shape now degrades to empty/default
/// values instead of a field-keyed 400.
/// </summary>
public static class DocsEndpoints
{
    public static void MapDocsEndpoints(this IEndpointRouteBuilder app)
    {
        var docs = app.MapGroup("/v1/docs");
        docs.MapGet("", (Delegate)ListDocuments);
        docs.MapDelete("", (Delegate)DeleteDocumentsBulk);
        docs.MapPost("/create", (Delegate)CreateDocuments);
        docs.MapPost("/generate", (Delegate)GenerateDocument);
        docs.MapPost("/upload", (Delegate)UploadDocuments);
        docs.MapPost("/url", (Delegate)GetDocumentsUrl);
        docs.MapGet("/{id}", (Delegate)GetDocument);
        docs.MapDelete("/{id}", (Delegate)DeleteDocument);
        docs.MapGet("/{id}/url", (Delegate)GetDocumentUrl);
    }

    private static async Task<IResult> ListDocuments(HttpContext ctx, IDispatcher dispatcher)
    {
        var principal = await RequestAuth.GateAsync(ctx);

        var entityId = QueryString(ctx, "entityId");
        var entityType = QueryString(ctx, "entityType");
        var type = QueryString(ctx, "type");
        var status = QueryString(ctx, "status");
        var page = QueryInt(ctx, "page") ?? 1;
        var limit = QueryInt(ctx, "limit") ?? 20;

        var filter = new DocumentListFilter(
            Guid.Parse(principal.Id), entityId, entityType, type, status, page, limit);
        var result = await dispatcher.Send(new ListDocumentsQuery(filter), ctx.RequestAborted);

        var data = new JsonArray([.. result.Items.Select(d => (JsonNode?)DocumentJson(d))]);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, data,
            extra: ApiEnvelope.Pagination(filter.Page, filter.Limit, result.Total));
    }

    private static async Task<IResult> GenerateDocument(
        HttpContext ctx, IDispatcher dispatcher, IJobQueue queue)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await ctx.Request.ReadValidatedJsonAsync<GenerateDocumentBody>(ctx.RequestAborted)
            ?? new GenerateDocumentBody(string.Empty, null, string.Empty, null, null, null);
        var dataJson = body.Data?.ToJsonString() ?? "{}";

        var result = await dispatcher.Send(
            new GenerateDocumentCommand(
                body.TemplateIdentifier, dataJson, body.Title, body.Description, body.EntityId, body.EntityType),
            ctx.RequestAborted);

        // Enqueued after dispatch returns — TransactionBehavior has already
        // committed the document row by now (GenerateDocumentHandler's doc
        // comment explains why enqueuing inside the handler would race that
        // commit). CancellationToken.None: this is post-commit work, the
        // document row already exists, and it must not be abandoned merely
        // because the client hung up (see Task 12's fix for the same pair).
        await queue.EnqueueAsync(
            new RenderDocumentCommand(result.DocumentId, body.TemplateIdentifier, dataJson, body.EntityId, body.EntityType),
            CancellationToken.None);

        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, new JsonObject
        {
            ["documentId"] = result.DocumentId.ToString(),
            ["jobId"] = result.JobId,
        });
    }

    private static async Task<IResult> GetDocument(HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var document = await dispatcher.Send(
            new GetDocumentQuery(Guid.Parse(id)), ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, DocumentJson(document, withVersions: true));
    }

    private static async Task<IResult> GetDocumentUrl(
        HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var url = await dispatcher.Send(
            new GetDocumentUrlQuery(Guid.Parse(id)), ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, new JsonObject { ["url"] = url });
    }

    private static async Task<IResult> DeleteDocument(
        HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        await dispatcher.Send(new DeleteDocumentCommand(Guid.Parse(id)), ctx.RequestAborted);
        // Frozen deleteDocument answers { deleted: true }, 200.
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, new JsonObject { ["deleted"] = true });
    }

    private static async Task<IResult> GetDocumentsUrl(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await ctx.Request.ReadValidatedJsonAsync<JsonObject>(ctx.RequestAborted);
        var items = ReadDocumentsPayload(body, "fileId");

        // Frozen: bare @Query params, no DTO — no whitelist, no coercion. A
        // non-numeric expiresInSeconds falls back to the signer default.
        var contentDisposition = QueryString(ctx, "contentDisposition");
        var expiresInSeconds = QueryInt(ctx, "expiresInSeconds");

        var results = await dispatcher.Send(
            new PresignDocumentsQuery(items, contentDisposition, expiresInSeconds), ctx.RequestAborted);
        var data = new JsonArray([.. results.Select(item => (JsonNode?)new JsonObject
        {
            ["bucket"] = item.Bucket,
            ["files"] = new JsonArray([.. item.Files.Select(f => (JsonNode?)new JsonObject
            {
                ["fileId"] = f.FileId,
                ["key"] = f.Key,
                ["url"] = f.Url,
            })]),
        })]);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, data);
    }

    /// <summary>Frozen stub: createDocuments has no implementation — bare 201 envelope.</summary>
    private static async Task<IResult> CreateDocuments(HttpContext ctx)
    {
        await RequestAuth.GateAsync(ctx);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    /// <summary>
    /// Frozen stub: the bulk DELETE ignored its payload entirely (the
    /// controller never called the service) — bare 200 envelope.
    /// </summary>
    private static async Task<IResult> DeleteDocumentsBulk(HttpContext ctx)
    {
        await RequestAuth.GateAsync(ctx);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, includeData: false);
    }

    private static async Task<IResult> UploadDocuments(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var form = await ctx.Request.ReadFormAsync();

        // Frozen FilesInterceptor('files'): a file under any other field name
        // was a Multer LIMIT_UNEXPECTED_FILE — 400 "Unexpected field".
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

        // Stream-through: the buffered multipart sections are handed to the
        // handler as streams — no per-file byte-array copy.
        var files = new List<UploadFileUpload>();
        foreach (var file in form.Files.GetFiles("files"))
        {
            files.Add(new UploadFileUpload(
                file.FileName, file.ContentType, file.Length, file.OpenReadStream()));
        }

        var result = await dispatcher.Send(
            new UploadDocumentsCommand(payload, entityId, entityType, files), ctx.RequestAborted);

        var s3Result = new JsonArray([.. result.S3Result.Select(item => (JsonNode?)new JsonObject
        {
            ["bucket"] = item.Bucket,
            ["files"] = new JsonArray([.. item.Files.Select(f => (JsonNode?)new JsonObject
            {
                ["fileId"] = f.FileId,
                ["filename"] = f.Filename,
                ["key"] = f.Key,
            })]),
        })]);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, new JsonObject
        {
            ["s3Result"] = s3Result,
            ["documentIds"] = new JsonArray([.. result.DocumentIds.Select(id => (JsonNode?)id.ToString())]),
        });
    }

    private static string? QueryString(HttpContext ctx, string name) =>
        ctx.Request.Query.TryGetValue(name, out var values) ? values[^1] : null;

    private static int? QueryInt(HttpContext ctx, string name) =>
        ctx.Request.Query.TryGetValue(name, out var values)
            && int.TryParse(values[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>The frozen DocumentsEntity JSON (TypeORM serialization).</summary>
    private static JsonObject DocumentJson(Document document, bool withVersions = false)
    {
        var json = new JsonObject
        {
            ["id"] = document.Id.ToString(),
            ["title"] = document.Title,
            ["description"] = document.Description,
            ["type"] = document.Type,
            ["status"] = document.Status,
            ["source"] = document.Source,
            ["entityId"] = document.EntityId,
            ["entityType"] = document.EntityType,
            ["createdBy"] = document.CreatedBy?.ToString(),
            ["createdAt"] = IsoTimestamp.Of(document.CreatedAt),
            ["deletedAt"] = IsoTimestamp.Of(document.DeletedAt),
            ["updatedAt"] = IsoTimestamp.Of(document.UpdatedAt),
        };
        if (withVersions)
        {
            json["versions"] = new JsonArray(
                [.. document.Versions.Select(v => (JsonNode?)VersionJson(v))]);
        }

        return json;
    }

    private static JsonObject VersionJson(DocumentVersion version) => new()
    {
        ["id"] = version.Id.ToString(),
        ["version"] = version.Version,
        ["filename"] = version.Filename,
        ["mimeType"] = version.MimeType,
        ["size"] = version.Size,
        ["createdAt"] = IsoTimestamp.Of(version.CreatedAt),
        ["storage"] = version.Storage is null ? null : new JsonObject
        {
            ["id"] = version.Storage.Id.ToString(),
            ["path"] = version.Storage.Path,
            ["bucket"] = version.Storage.Bucket,
            ["region"] = version.Storage.Region,
            ["etag"] = version.Storage.ETag,
            ["createdAt"] = IsoTimestamp.Of(version.Storage.CreatedAt),
            ["updatedAt"] = IsoTimestamp.Of(version.Storage.UpdatedAt),
        },
    };

    /// <summary>The frozen generateDocument body shape (data is a raw JSON object, serialized back to a string for the command).</summary>
    private sealed record GenerateDocumentBody(
        string TemplateIdentifier,
        JsonObject? Data,
        string Title,
        string? Description,
        string? EntityId,
        string? EntityType);

    /// <summary>
    /// The frozen DocumentsPayloadDto surface: documents is @IsArray (no
    /// ArrayNotEmpty — an empty array is valid and answers []), items carry
    /// bucket + files[].folderName + files[].fileInfo.fileId.
    /// </summary>
    private static List<PresignItem> ReadDocumentsPayload(JsonObject? body, string leafField) =>
        [.. ReadBucketFilesArray(body?["documents"] as JsonArray, leafField)
            .Select(item => new PresignItem(
                item.Bucket, [.. item.Files.Select(f => new PresignFileRef(f.FolderName, f.LeafValue))]))];

    /// <summary>
    /// The frozen UploadDocumentPayloadDto surface: the multipart "payload"
    /// field is a JSON string (@Transform JSON.parse — an unparseable value
    /// escaped the pipe as a bare 500, pinned), an array of
    /// bucket + files[].folderName + files[].fileInfo.fileName.
    /// </summary>
    private static List<UploadPayloadItem> ReadUploadPayload(IFormCollection form)
    {
        if (!form.TryGetValue("payload", out var payloadValues))
        {
            return [];
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payloadValues[^1] ?? string.Empty);
        }
        catch (JsonException)
        {
            // Frozen: JSON.parse threw inside @Transform — a bare 500.
            // The handler is the mapper now: an unparseable payload string
            // is our bug, not the caller's malformed request.
            throw new InvalidOperationException("Unable to parse the 'payload' field as JSON.");
        }

        return [.. ReadBucketFilesArray(node as JsonArray, "fileName")
            .Select(item => new UploadPayloadItem(
                item.Bucket, [.. item.Files.Select(f => new UploadPayloadFile(f.FolderName, f.LeafValue))]))];
    }

    private sealed record RawFile(string FolderName, string LeafValue);

    private sealed record RawItem(string Bucket, IReadOnlyList<RawFile> Files);

    /// <summary>
    /// The one traversal behind both frozen bucket+files payload shapes
    /// (documents/fileId and payload/fileName). Tolerant: a malformed entry
    /// degrades to empty strings rather than a field-keyed failure — full
    /// shape validation for this nested payload is a Task 7 reshape.
    /// </summary>
    private static List<RawItem> ReadBucketFilesArray(JsonArray? array, string leafField)
    {
        var items = new List<RawItem>();
        if (array is null)
        {
            return items;
        }

        foreach (var entry in array)
        {
            if (entry is not JsonObject item)
            {
                continue;
            }

            var bucket = item["bucket"]?.GetValueKind() == JsonValueKind.String
                ? item["bucket"]!.GetValue<string>()
                : string.Empty;

            var files = new List<RawFile>();
            if (item["files"] is JsonArray fileArray)
            {
                foreach (var fileNode in fileArray)
                {
                    if (fileNode is not JsonObject file)
                    {
                        continue;
                    }

                    var folderName = file["folderName"]?.GetValueKind() == JsonValueKind.String
                        ? file["folderName"]!.GetValue<string>()
                        : string.Empty;
                    var leaf = file["fileInfo"] is JsonObject fileInfo
                        && fileInfo[leafField]?.GetValueKind() == JsonValueKind.String
                        ? fileInfo[leafField]!.GetValue<string>()
                        : string.Empty;
                    files.Add(new RawFile(folderName, leaf));
                }
            }

            items.Add(new RawItem(bucket, files));
        }

        return items;
    }
}
