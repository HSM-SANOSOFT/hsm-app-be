using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hsm.Application.Auth;
using Hsm.Application.Docs;
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

    private static async Task<IResult> ListDocuments(HttpContext ctx, ListDocumentsHandler handler)
    {
        var principal = await AuthorizeAsync(ctx);

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
        var (items, total) = await handler.HandleAsync(filter);

        var data = new JsonArray([.. items.Select(d => (JsonNode?)DocumentJson(d))]);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, data, extra: PaginationMeta(filter.Page, filter.Limit, total));
    }

    private static async Task<IResult> GenerateDocument(HttpContext ctx, GenerateDocumentHandler handler)
    {
        var principal = await AuthorizeAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var templateIdentifier = body.RequiredString("templateIdentifier");
        var data = body.RequiredObject("data");
        var title = body.RequiredString("title");
        var description = body.OptionalString("description");
        var entityId = body.OptionalString("entityId");
        var entityType = body.OptionalString("entityType");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var result = await handler.HandleAsync(
            new GenerateDocumentHandler.Command(
                templateIdentifier,
                data?.ToJsonString() ?? "{}",
                title,
                description,
                entityId,
                entityType),
            Guid.Parse(principal.Id));
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, new JsonObject
        {
            ["documentId"] = result.DocumentId.ToString(),
            ["jobId"] = result.JobId,
        });
    }

    private static async Task<IResult> GetDocument(HttpContext ctx, string id, GetDocumentHandler handler)
    {
        var principal = await AuthorizeAsync(ctx);
        var document = await handler.HandleAsync(ParseDocumentId(id), Guid.Parse(principal.Id));
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, DocumentJson(document, withVersions: true));
    }

    private static async Task<IResult> GetDocumentUrl(
        HttpContext ctx, string id, GetDocumentUrlHandler handler)
    {
        var principal = await AuthorizeAsync(ctx);
        var url = await handler.HandleAsync(ParseDocumentId(id), Guid.Parse(principal.Id));
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, new JsonObject { ["url"] = url });
    }

    private static async Task<IResult> DeleteDocument(
        HttpContext ctx, string id, DeleteDocumentHandler handler)
    {
        var principal = await AuthorizeAsync(ctx);
        await handler.HandleAsync(ParseDocumentId(id), Guid.Parse(principal.Id));
        // Frozen deleteDocument answers { deleted: true }, 200.
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, new JsonObject { ["deleted"] = true });
    }

    private static async Task<IResult> GetDocumentsUrl(HttpContext ctx, PresignDocumentsHandler handler)
    {
        await AuthorizeAsync(ctx);

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

        var results = await handler.HandleAsync(items!, contentDisposition, expiresInSeconds);
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
        await AuthorizeAsync(ctx);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    /// <summary>
    /// Frozen stub: the bulk DELETE ignored its payload entirely (the
    /// controller never called the service) — bare 200 envelope.
    /// </summary>
    private static async Task<IResult> DeleteDocumentsBulk(HttpContext ctx)
    {
        await AuthorizeAsync(ctx);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, includeData: false);
    }

    private static async Task<IResult> UploadDocuments(HttpContext ctx, UploadDocumentsHandler handler)
    {
        var principal = await AuthorizeAsync(ctx);

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

        var files = new List<UploadDocumentsHandler.FileUpload>();
        foreach (var file in form.Files.GetFiles("files"))
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            files.Add(new UploadDocumentsHandler.FileUpload(
                file.FileName, file.ContentType, file.Length, buffer.ToArray()));
        }

        var result = await handler.HandleAsync(
            new UploadDocumentsHandler.Command(payload, entityId, entityType, files),
            Guid.Parse(principal.Id));

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

    /// <summary>The frozen guard chain: authenticated, any role, onboarding complete.</summary>
    private static async Task<AuthPrincipal> AuthorizeAsync(HttpContext ctx)
    {
        var principal = await RequestAuth.AuthenticateAsync(ctx, TokenKind.Access);
        RequestAuth.RequireRoles(ctx, principal);
        await RequestAuth.RequireOnboardingCompletedAsync(ctx, principal);
        return principal;
    }

    /// <summary>
    /// The frozen docs routes had NO ParseUUIDPipe: a malformed id reached
    /// PostgreSQL, whose uuid-cast failure surfaced as a bare 500. Pinned.
    /// </summary>
    private static Guid ParseDocumentId(string id) =>
        Guid.TryParse(id, out var parsed)
            ? parsed
            : throw new ApiException(500, "Internal server error");

    private static string? NullableQuery(HttpContext ctx, string name) =>
        ctx.Request.Query.TryGetValue(name, out var values) ? values[^1] : null;

    /// <summary>
    /// The frozen buildPaginationMeta shape: metadata.extra.pagination with
    /// query-driven page/pageSize and computed totals.
    /// </summary>
    private static JsonObject PaginationMeta(int page, int pageSize, int totalItems) => new()
    {
        ["pagination"] = new JsonObject
        {
            ["page"] = page,
            ["pageSize"] = pageSize,
            ["totalItems"] = totalItems,
            ["totalPages"] = pageSize > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 0,
        },
    };

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
            ["createdAt"] = Iso(document.CreatedAt),
            ["deletedAt"] = document.DeletedAt is { } deleted ? Iso(deleted) : null,
            ["updatedAt"] = Iso(document.UpdatedAt),
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
        ["createdAt"] = Iso(version.CreatedAt),
        ["storage"] = version.Storage is null ? null : new JsonObject
        {
            ["id"] = version.Storage.Id.ToString(),
            ["path"] = version.Storage.Path,
            ["bucket"] = version.Storage.Bucket,
            ["region"] = version.Storage.Region,
            ["etag"] = version.Storage.ETag,
            ["createdAt"] = Iso(version.Storage.CreatedAt),
            ["updatedAt"] = Iso(version.Storage.UpdatedAt),
        },
    };

    private static string Iso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// The frozen DocumentsPayloadDto surface: documents is @IsArray (no
    /// ArrayNotEmpty — an empty array is valid and answers []), items carry
    /// bucket + files[].folderName + files[].fileInfo.fileId.
    /// </summary>
    private static List<PresignDocumentsHandler.Item>? ReadDocumentsPayload(BodyValidator body)
    {
        var node = body.RawNode("documents");
        if (node is not JsonArray array)
        {
            body.AddFailure("documents", "isArray", "documents must be an array");
            return null;
        }

        var items = new List<PresignDocumentsHandler.Item>();
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject item)
            {
                body.AddFailure(
                    $"documents.{i}", "nestedValidation",
                    "nested property documents must be either object or array");
                continue;
            }

            var bucket = ReadNonEmptyString(body, item, $"documents.{i}", "bucket");
            var files = new List<PresignDocumentsHandler.FileRef>();
            if (item["files"] is not JsonArray fileArray)
            {
                body.AddFailure($"documents.{i}.files", "isArray", $"documents.{i}.files must be an array");
            }
            else
            {
                for (var j = 0; j < fileArray.Count; j++)
                {
                    if (fileArray[j] is not JsonObject file)
                    {
                        body.AddFailure(
                            $"documents.{i}.files.{j}", "nestedValidation",
                            "nested property files must be either object or array");
                        continue;
                    }

                    var folderName = ReadNonEmptyString(body, file, $"documents.{i}.files.{j}", "folderName");
                    if (file["fileInfo"] is not JsonObject fileInfo)
                    {
                        body.AddFailure(
                            $"documents.{i}.files.{j}.fileInfo", "isObject",
                            $"documents.{i}.files.{j}.fileInfo must be an object");
                        continue;
                    }

                    var fileId = ReadNonEmptyString(body, fileInfo, $"documents.{i}.files.{j}.fileInfo", "fileId");
                    files.Add(new PresignDocumentsHandler.FileRef(folderName, fileId));
                }
            }

            items.Add(new PresignDocumentsHandler.Item(bucket, files));
        }

        return items;
    }

    /// <summary>
    /// The frozen UploadDocumentPayloadDto surface: the multipart "payload"
    /// field is a JSON string (@Transform JSON.parse — an unparseable value
    /// escaped the pipe as a bare 500, pinned), an array of
    /// bucket + files[].folderName + files[].fileInfo.fileName.
    /// </summary>
    private static List<UploadDocumentsHandler.PayloadItem> ReadUploadPayload(IFormCollection form)
    {
        var failures = new List<(string Field, string Key, string Message)>();
        foreach (var field in form.Keys)
        {
            if (field is not ("payload" or "entityId" or "entityType"))
            {
                failures.Add((field, "whitelistValidation", $"property {field} should not exist"));
            }
        }

        JsonNode? node = null;
        if (!form.TryGetValue("payload", out var payloadValues))
        {
            failures.Add(("payload", "isArray", "payload must be an array"));
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

        var items = new List<UploadDocumentsHandler.PayloadItem>();
        if (node is not null)
        {
            if (node is not JsonArray array)
            {
                failures.Add(("payload", "isArray", "payload must be an array"));
            }
            else
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is not JsonObject item)
                    {
                        failures.Add((
                            $"payload.{i}", "nestedValidation",
                            "nested property payload must be either object or array"));
                        continue;
                    }

                    var bucket = ReadNonEmptyStringInto(failures, item, $"payload.{i}", "bucket");
                    var files = new List<UploadDocumentsHandler.PayloadFile>();
                    if (item["files"] is not JsonArray fileArray)
                    {
                        failures.Add(($"payload.{i}.files", "isArray", $"payload.{i}.files must be an array"));
                    }
                    else
                    {
                        for (var j = 0; j < fileArray.Count; j++)
                        {
                            if (fileArray[j] is not JsonObject file)
                            {
                                failures.Add((
                                    $"payload.{i}.files.{j}", "nestedValidation",
                                    "nested property files must be either object or array"));
                                continue;
                            }

                            var folderName = ReadNonEmptyStringInto(
                                failures, file, $"payload.{i}.files.{j}", "folderName");
                            if (file["fileInfo"] is not JsonObject fileInfo)
                            {
                                failures.Add((
                                    $"payload.{i}.files.{j}.fileInfo", "isObject",
                                    $"payload.{i}.files.{j}.fileInfo must be an object"));
                                continue;
                            }

                            var fileName = ReadNonEmptyStringInto(
                                failures, fileInfo, $"payload.{i}.files.{j}.fileInfo", "fileName");
                            files.Add(new UploadDocumentsHandler.PayloadFile(folderName, fileName));
                        }
                    }

                    items.Add(new UploadDocumentsHandler.PayloadItem(bucket, files));
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new ApiValidationException(
                [.. failures.Select(f => f.Message)],
                [.. failures
                    .GroupBy(f => f.Field)
                    .Select(g => (g.Key, (IReadOnlyList<string>)[.. g.Select(f => f.Key).Distinct()]))]);
        }

        return items;
    }

    private static string ReadNonEmptyString(BodyValidator body, JsonObject parent, string path, string field)
    {
        var node = parent[field];
        if (node is null
            || node.GetValueKind() != JsonValueKind.String
            || node.GetValue<string>().Length == 0)
        {
            body.AddFailure($"{path}.{field}", "isNotEmpty", $"{path}.{field} should not be empty");
            return string.Empty;
        }

        return node.GetValue<string>();
    }

    private static string ReadNonEmptyStringInto(
        List<(string Field, string Key, string Message)> failures, JsonObject parent, string path, string field)
    {
        var node = parent[field];
        if (node is null
            || node.GetValueKind() != JsonValueKind.String
            || node.GetValue<string>().Length == 0)
        {
            failures.Add(($"{path}.{field}", "isNotEmpty", $"{path}.{field} should not be empty"));
            return string.Empty;
        }

        return node.GetValue<string>();
    }
}
