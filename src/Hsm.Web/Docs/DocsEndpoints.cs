using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hsm.Application;
using Hsm.Application.Abstractions;
using Hsm.Application.Docs;
using Hsm.Application.Docs.Commands.DeleteDocument;
using Hsm.Application.Docs.Commands.GenerateDocument;
using Hsm.Application.Docs.Commands.UploadDocuments;
using Hsm.Application.Docs.Queries.GetDocument;
using Hsm.Application.Docs.Queries.GetDocumentUrl;
using Hsm.Application.Docs.Queries.ListDocuments;
using Hsm.Application.Docs.Queries.PresignDocuments;
using Hsm.Application.Errors;
using Hsm.Domain.Docs;
using Hsm.Web.Api;
using Hsm.Web.Auth;

namespace Hsm.Web.Docs;

/// <summary>
/// The nine frozen /v1/docs operations (docs.controller.ts). Every route is
/// @Roles() with no arguments — any authenticated, onboarded user. POST
/// routes return 201 (the frozen runtime default; the OpenAPI snapshot
/// under-documented these as 200). Two frozen stubs are pinned as stubs:
/// POST /create and the bulk DELETE answered the bare success envelope
/// without ever touching storage.
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

        var query = QueryValidator.Read(ctx);
        var entityId = query.OptionalString("entityId");
        var entityType = query.OptionalString("entityType");
        var type = query.OptionalEnum("type", DocumentTypes.All);
        var status = query.OptionalEnum("status", DocumentStatuses.All);
        var page = query.OptionalInt("page", min: 1);
        var limit = query.OptionalInt("limit", min: 1, max: 100);
        query.RejectUnknownParams();
        query.ThrowIfInvalid();

        var filter = new DocumentListFilter(
            Guid.Parse(principal.Id), entityId, entityType, type, status, page ?? 1, limit ?? 20);
        var result = await dispatcher.Send(new ListDocumentsQuery(filter), ctx.RequestAborted);

        var data = new JsonArray([.. result.Items.Select(d => (JsonNode?)DocumentJson(d))]);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, data,
            extra: ApiEnvelope.Pagination(filter.Page, filter.Limit, result.Total));
    }

    private static async Task<IResult> GenerateDocument(
        HttpContext ctx, IDispatcher dispatcher, IDocsJobDispatcher queue)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var templateIdentifier = body.RequiredString("templateIdentifier");
        var data = body.RequiredObject("data");
        var title = body.RequiredString("title");
        var description = body.OptionalString("description");
        var entityId = body.OptionalString("entityId");
        var entityType = body.OptionalString("entityType");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var dataJson = data?.ToJsonString() ?? "{}";
        var result = await dispatcher.Send(
            new GenerateDocumentCommand(templateIdentifier, dataJson, title, description, entityId, entityType),
            ctx.RequestAborted);

        // Enqueued after dispatch returns — TransactionBehavior has already
        // committed the document row by now (GenerateDocumentHandler's doc
        // comment explains why enqueuing inside the handler would race that
        // commit). CancellationToken.None: this is post-commit work, the
        // document row already exists, and it must not be abandoned merely
        // because the client hung up (see Task 12's fix for the same pair).
        await queue.EnqueueGenerateDocumentAsync(
            result.JobId,
            new GenerateDocumentJob(result.DocumentId, templateIdentifier, dataJson, entityId, entityType),
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
            new GetDocumentQuery(RouteParams.UnpipedUuid(id)), ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, DocumentJson(document, withVersions: true));
    }

    private static async Task<IResult> GetDocumentUrl(
        HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var url = await dispatcher.Send(
            new GetDocumentUrlQuery(RouteParams.UnpipedUuid(id)), ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, new JsonObject { ["url"] = url });
    }

    private static async Task<IResult> DeleteDocument(
        HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        await dispatcher.Send(new DeleteDocumentCommand(RouteParams.UnpipedUuid(id)), ctx.RequestAborted);
        // Frozen deleteDocument answers { deleted: true }, 200.
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, new JsonObject { ["deleted"] = true });
    }

    private static async Task<IResult> GetDocumentsUrl(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var items = ReadDocumentsPayload(body);
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        // Frozen: bare @Query params, no DTO — no whitelist, no coercion. A
        // non-numeric expiresInSeconds falls back to the signer default.
        var contentDisposition = NullableQuery(ctx, "contentDisposition");
        int? expiresInSeconds = int.TryParse(
            NullableQuery(ctx, "expiresInSeconds"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

        var results = await dispatcher.Send(
            new PresignDocumentsQuery(items!, contentDisposition, expiresInSeconds), ctx.RequestAborted);
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
        if (form.Files.Any(f => f.Name != "files"))
        {
            throw ApiException.BadRequest("Unexpected field");
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

    private static string? NullableQuery(HttpContext ctx, string name) =>
        ctx.Request.Query.TryGetValue(name, out var values) ? values[^1] : null;

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

    /// <summary>
    /// The frozen DocumentsPayloadDto surface: documents is @IsArray (no
    /// ArrayNotEmpty — an empty array is valid and answers []), items carry
    /// bucket + files[].folderName + files[].fileInfo.fileId.
    /// </summary>
    private static List<PresignItem>? ReadDocumentsPayload(BodyValidator body)
    {
        var node = body.RawNode("documents");
        if (node is not JsonArray array)
        {
            body.AddFailure("documents", "isArray", "documents must be an array");
            return null;
        }

        return [.. ReadBucketFilesArray(array, "documents", "fileId", body.Failures)
            .Select(item => new PresignItem(
                item.Bucket,
                [.. item.Files.Select(f => new PresignFileRef(f.FolderName, f.LeafValue))]))];
    }

    /// <summary>
    /// The frozen UploadDocumentPayloadDto surface: the multipart "payload"
    /// field is a JSON string (@Transform JSON.parse — an unparseable value
    /// escaped the pipe as a bare 500, pinned), an array of
    /// bucket + files[].folderName + files[].fileInfo.fileName.
    /// </summary>
    private static List<UploadPayloadItem> ReadUploadPayload(IFormCollection form)
    {
        var failures = new ValidationFailures();
        foreach (var field in form.Keys)
        {
            if (field is not ("payload" or "entityId" or "entityType"))
            {
                failures.Add(field, "whitelistValidation", $"property {field} should not exist");
            }
        }

        JsonNode? node = null;
        if (!form.TryGetValue("payload", out var payloadValues))
        {
            failures.Add("payload", "isArray", "payload must be an array");
        }
        else
        {
            try
            {
                node = JsonNode.Parse(payloadValues[^1] ?? string.Empty);
            }
            catch (JsonException)
            {
                // Frozen: JSON.parse threw inside @Transform — a bare 500.
                throw new ApiException(500, "Internal server error");
            }
        }

        var items = new List<UploadPayloadItem>();
        if (node is not null)
        {
            if (node is not JsonArray array)
            {
                failures.Add("payload", "isArray", "payload must be an array");
            }
            else
            {
                items = [.. ReadBucketFilesArray(array, "payload", "fileName", failures)
                    .Select(item => new UploadPayloadItem(
                        item.Bucket,
                        [.. item.Files.Select(f => new UploadPayloadFile(f.FolderName, f.LeafValue))]))];
            }
        }

        failures.ThrowIfAny();
        return items;
    }

    private sealed record RawFile(string FolderName, string LeafValue);

    private sealed record RawItem(string Bucket, IReadOnlyList<RawFile> Files);

    /// <summary>
    /// The one traversal behind both frozen bucket+files payload shapes
    /// (documents/fileId and payload/fileName), reporting into the caller's
    /// failure sink.
    /// </summary>
    private static List<RawItem> ReadBucketFilesArray(
        JsonArray array, string root, string leafField, ValidationFailures failures)
    {
        var items = new List<RawItem>();
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject item)
            {
                failures.Add(
                    $"{root}.{i}", "nestedValidation",
                    $"nested property {root} must be either object or array");
                continue;
            }

            var bucket = new NestedValidator(item, $"{root}.{i}", failures).NonEmptyString("bucket");
            var files = new List<RawFile>();
            if (item["files"] is not JsonArray fileArray)
            {
                failures.Add($"{root}.{i}.files", "isArray", $"{root}.{i}.files must be an array");
            }
            else
            {
                for (var j = 0; j < fileArray.Count; j++)
                {
                    if (fileArray[j] is not JsonObject file)
                    {
                        failures.Add(
                            $"{root}.{i}.files.{j}", "nestedValidation",
                            "nested property files must be either object or array");
                        continue;
                    }

                    var folderName = new NestedValidator(file, $"{root}.{i}.files.{j}", failures)
                        .NonEmptyString("folderName");
                    if (file["fileInfo"] is not JsonObject fileInfo)
                    {
                        failures.Add(
                            $"{root}.{i}.files.{j}.fileInfo", "isObject",
                            $"{root}.{i}.files.{j}.fileInfo must be an object");
                        continue;
                    }

                    var leaf = new NestedValidator(fileInfo, $"{root}.{i}.files.{j}.fileInfo", failures)
                        .NonEmptyString(leafField);
                    files.Add(new RawFile(folderName, leaf));
                }
            }

            items.Add(new RawItem(bucket, files));
        }

        return items;
    }
}
