using Hsm.Application.Errors;
using Hsm.Application.Ports;
using Hsm.Domain.Docs;

namespace Hsm.Application.Docs;

/// <summary>GET /v1/docs (frozen listDocuments): createdBy-scoped page.</summary>
public sealed class ListDocumentsHandler(IDocumentStore store)
{
    public Task<(IReadOnlyList<Document> Items, int Total)> HandleAsync(
        DocumentListFilter filter, CancellationToken ct = default) => store.ListAsync(filter, ct);
}

/// <summary>
/// POST /v1/docs/generate (frozen generateDocument): persists a PENDING
/// GENERATED/TEMPLATE document and enqueues the render job — the response
/// carries the ids, the work happens behind the dispatcher port.
/// </summary>
public sealed class GenerateDocumentHandler(IDocumentStore store, IDocsJobDispatcher dispatcher)
{
    public sealed record Command(
        string TemplateIdentifier,
        string DataJson,
        string Title,
        string? Description,
        string? EntityId,
        string? EntityType);

    public sealed record Result(Guid DocumentId, string JobId);

    public async Task<Result> HandleAsync(Command command, Guid? userId, CancellationToken ct = default)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Title = command.Title,
            Description = command.Description,
            Type = DocumentTypes.Generated,
            Status = DocumentStatuses.Pending,
            Source = DocumentSources.Template,
            CreatedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await store.AddAsync(document, ct);

        var jobId = await dispatcher.EnqueueGenerateDocumentAsync(
            new GenerateDocumentJob(
                document.Id,
                command.TemplateIdentifier,
                command.DataJson,
                command.EntityId,
                command.EntityType),
            ct);
        return new Result(document.Id, jobId);
    }
}

/// <summary>GET /v1/docs/{id} (frozen getDocument): owner-scoped, 404 otherwise.</summary>
public sealed class GetDocumentHandler(IDocumentStore store)
{
    public async Task<Document> HandleAsync(Guid id, Guid userId, CancellationToken ct = default)
        => await store.FindWithVersionsAsync(id, userId, ct)
            ?? throw ApiException.NotFound($"Document '{id}' not found");
}

/// <summary>
/// GET /v1/docs/{id}/url (frozen getDocumentUrl): presigns the LATEST
/// version's object, inline disposition, frozen default expiry.
/// </summary>
public sealed class GetDocumentUrlHandler(IDocumentStore store, IObjectStorage storage)
{
    public async Task<string> HandleAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var document = await store.FindWithVersionsAsync(id, userId, ct)
            ?? throw ApiException.NotFound($"Document '{id}' not found");

        var latest = document.Versions.OrderByDescending(v => v.Version).FirstOrDefault();
        if (latest?.Storage is null)
        {
            throw ApiException.NotFound($"No generated file found for document '{id}'");
        }

        var (folderName, fileId) = StorageKeys.Split(latest.Storage.Path);
        var url = await storage.PresignGetAsync(
            StorageKeys.MakeKey(folderName, fileId),
            expiresIn: null,
            contentDisposition: "inline",
            bucket: latest.Storage.Bucket,
            cancellationToken: ct);
        return url.AbsoluteUri;
    }
}

/// <summary>
/// DELETE /v1/docs/{id} (frozen deleteDocument): owner-scoped 404, then soft
/// delete + best-effort blob deletion of every version's object. Frozen link
/// semantics preserved: links neither block the delete nor cascade.
/// </summary>
public sealed class DeleteDocumentHandler(IDocumentStore store, IObjectStorage storage)
{
    public async Task HandleAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var document = await store.FindWithVersionsAsync(id, userId, ct)
            ?? throw ApiException.NotFound($"Document '{id}' not found");

        await store.SoftDeleteAsync(id, ct);

        // Independent blob deletes run concurrently (bounded), each still
        // best-effort: the frozen deleteFiles swallows per-object failures
        // (logged only) — the API still answers { deleted: true }.
        using var throttle = new SemaphoreSlim(4);
        await Task.WhenAll(document.Versions
            .Where(version => version.Storage is not null)
            .Select(async version =>
            {
                await throttle.WaitAsync(ct);
                try
                {
                    var (folderName, fileId) = StorageKeys.Split(version.Storage!.Path);
                    await storage.DeleteAsync(
                        StorageKeys.MakeKey(folderName, fileId), version.Storage.Bucket, ct);
                }
                catch (ObjectStorageException)
                {
                    // Swallowed, as in the frozen path.
                }
                finally
                {
                    throttle.Release();
                }
            }));
    }
}

/// <summary>POST /v1/docs/url + the frozen S3Service.generatePresignedUrls shapes.</summary>
public sealed class PresignDocumentsHandler(IObjectStorage storage)
{
    public sealed record FileRef(string FolderName, string FileId);

    public sealed record Item(string Bucket, IReadOnlyList<FileRef> Files);

    public sealed record PresignedFile(string FileId, string Key, string Url);

    public sealed record PresignedItem(string Bucket, IReadOnlyList<PresignedFile> Files);

    public async Task<IReadOnlyList<PresignedItem>> HandleAsync(
        IReadOnlyList<Item> items,
        string? contentDisposition,
        int? expiresInSeconds,
        CancellationToken ct = default)
    {
        var results = new List<PresignedItem>();
        foreach (var item in items)
        {
            var files = new List<PresignedFile>();
            foreach (var file in item.Files)
            {
                var key = StorageKeys.MakeKey(file.FolderName, file.FileId);
                var url = await storage.PresignGetAsync(
                    key,
                    expiresInSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                    contentDisposition ?? "inline",
                    item.Bucket,
                    ct);
                files.Add(new PresignedFile(file.FileId, key, url.AbsoluteUri));
            }

            results.Add(new PresignedItem(item.Bucket, files));
        }

        return results;
    }
}

/// <summary>
/// POST /v1/docs/upload (frozen uploadDocuments): filename-matched multipart
/// files, blob uploads first, then ONE transaction for the whole document
/// graph — the frozen guarantee that a mid-loop failure cannot leave a
/// half-written graph.
/// </summary>
public sealed class UploadDocumentsHandler(IDocumentStore store, IObjectStorage storage)
{
    /// <summary>
    /// One multipart file part. <paramref name="Content"/> is the caller's
    /// buffered form stream — read once here, disposed with the request.
    /// </summary>
    public sealed record FileUpload(string FileName, string ContentType, long Size, Stream Content);

    public sealed record PayloadFile(string FolderName, string FileName);

    public sealed record PayloadItem(string Bucket, IReadOnlyList<PayloadFile> Files);

    public sealed record Command(
        IReadOnlyList<PayloadItem> Payload,
        string? EntityId,
        string? EntityType,
        IReadOnlyList<FileUpload> Files);

    public sealed record UploadedFile(string FileId, string Filename, string Key);

    public sealed record UploadedItem(string Bucket, IReadOnlyList<UploadedFile> Files);

    public sealed record Result(IReadOnlyList<UploadedItem> S3Result, IReadOnlyList<Guid> DocumentIds);

    public async Task<Result> HandleAsync(Command command, Guid? userId, CancellationToken ct = default)
    {
        // Frozen matching: a queue per trimmed original filename; each
        // payload entry consumes one file; leftovers are an error either way.
        var fileQueues = new Dictionary<string, Queue<FileUpload>>(StringComparer.Ordinal);
        var fileByName = new Dictionary<string, FileUpload>(StringComparer.Ordinal);
        foreach (var file in command.Files)
        {
            var name = file.FileName.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            if (!fileQueues.TryGetValue(name, out var queue))
            {
                queue = new Queue<FileUpload>();
                fileQueues[name] = queue;
            }

            queue.Enqueue(file);
            fileByName.TryAdd(name, file);
        }

        // Per-item grouped matching — the bucket stays derivable from the
        // payload item, so only (folderName, file) pairs are carried.
        var matched = new List<IReadOnlyList<(string FolderName, FileUpload File)>>();
        foreach (var item in command.Payload)
        {
            var itemFiles = new List<(string FolderName, FileUpload File)>();
            foreach (var payloadFile in item.Files)
            {
                if (!fileQueues.TryGetValue(payloadFile.FileName, out var queue) || queue.Count == 0)
                {
                    throw new ApiException(
                        500,
                        $"No uploaded file matched payload filename=\"{payloadFile.FileName}\" (bucket=\"{item.Bucket}\")",
                        errorLabel: "Internal Server Error");
                }

                var file = queue.Dequeue();
                if (queue.Count == 0)
                {
                    fileQueues.Remove(payloadFile.FileName);
                }

                itemFiles.Add((payloadFile.FolderName, file));
            }

            matched.Add(itemFiles);
        }

        if (fileQueues.Count > 0)
        {
            throw new ApiException(
                500,
                $"Uploaded files not referenced in payload: {string.Join(", ", fileQueues.Keys)}",
                errorLabel: "Internal Server Error");
        }

        // Blob uploads first (frozen order), grouped per payload item;
        // independent puts run concurrently (bounded) with result order
        // preserved by index.
        var s3Result = new List<UploadedItem>();
        var records = new List<UploadedDocumentRecord>();
        var puts = new List<Task>();
        using var throttle = new SemaphoreSlim(4);
        foreach (var (item, itemFiles) in command.Payload.Zip(matched))
        {
            var uploaded = new UploadedFile[itemFiles.Count];
            for (var i = 0; i < itemFiles.Count; i++)
            {
                var (folderName, file) = itemFiles[i];
                var fileId = Guid.NewGuid();
                var key = StorageKeys.MakeKey(folderName, fileId.ToString());
                uploaded[i] = new UploadedFile(fileId.ToString(), file.FileName, key);
                puts.Add(PutThrottledAsync(throttle, key, file, item.Bucket, ct));

                // Frozen quirk preserved: mimeType/size come from the FIRST
                // file carrying this name, not necessarily the matched one.
                var original = fileByName.GetValueOrDefault(file.FileName.Trim());
                records.Add(new UploadedDocumentRecord(
                    Title: file.FileName,
                    Filename: file.FileName,
                    MimeType: original?.ContentType,
                    Size: original?.Size,
                    FileId: fileId,
                    Key: key,
                    Bucket: item.Bucket,
                    CreatedBy: userId,
                    EntityId: command.EntityId,
                    EntityType: command.EntityType));
            }

            s3Result.Add(new UploadedItem(item.Bucket, uploaded));
        }

        await Task.WhenAll(puts);

        var documentIds = await store.AddUploadedDocumentsAsync(records, ct);
        return new Result(s3Result, documentIds);
    }

    private async Task PutThrottledAsync(
        SemaphoreSlim throttle, string key, FileUpload file, string bucket, CancellationToken ct)
    {
        await throttle.WaitAsync(ct);
        try
        {
            await storage.PutAsync(key, file.Content, file.ContentType, bucket, ct);
        }
        finally
        {
            throttle.Release();
        }
    }
}
