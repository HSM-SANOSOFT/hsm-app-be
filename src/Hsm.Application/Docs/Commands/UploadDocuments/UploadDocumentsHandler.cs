using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Application.Ports;

namespace Hsm.Application.Docs.Commands.UploadDocuments;

public sealed class UploadDocumentsHandler(IDocumentStore store, IObjectStorage storage, ICurrentPrincipal principal)
    : IRequestHandler<UploadDocumentsCommand, UploadDocumentsResult>
{
    public async Task<UploadDocumentsResult> HandleAsync(UploadDocumentsCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = principal.Actor ?? throw new UnauthorizedException();
        var userId = Guid.Parse(actor.Id);

        // Frozen matching: a queue per trimmed original filename; each
        // payload entry consumes one file; leftovers are an error either way.
        var fileQueues = new Dictionary<string, Queue<UploadFileUpload>>(StringComparer.Ordinal);
        var fileByName = new Dictionary<string, UploadFileUpload>(StringComparer.Ordinal);
        foreach (var file in request.Files)
        {
            var name = file.FileName.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            if (!fileQueues.TryGetValue(name, out var queue))
            {
                queue = new Queue<UploadFileUpload>();
                fileQueues[name] = queue;
            }

            queue.Enqueue(file);
            fileByName.TryAdd(name, file);
        }

        // Per-item grouped matching — the bucket stays derivable from the
        // payload item, so only (folderName, file) pairs are carried.
        var matched = new List<IReadOnlyList<(string FolderName, UploadFileUpload File)>>();
        foreach (var item in request.Payload)
        {
            var itemFiles = new List<(string FolderName, UploadFileUpload File)>();
            foreach (var payloadFile in item.Files)
            {
                if (!fileQueues.TryGetValue(payloadFile.FileName, out var queue) || queue.Count == 0)
                {
                    throw new ValidationException(
                        [
                            new FluentValidation.Results.ValidationFailure(
                                "files",
                                $"No uploaded file matched payload filename=\"{payloadFile.FileName}\" (bucket=\"{item.Bucket}\")"),
                        ]);
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
            throw new ValidationException(
                [
                    new FluentValidation.Results.ValidationFailure(
                        "files",
                        $"Uploaded files not referenced in payload: {string.Join(", ", fileQueues.Keys)}"),
                ]);
        }

        // Blob uploads first (frozen order), grouped per payload item;
        // independent puts run concurrently (bounded) with result order
        // preserved by index.
        var s3Result = new List<UploadedItem>();
        var records = new List<UploadedDocumentRecord>();
        var puts = new List<Task>();
        using var throttle = new SemaphoreSlim(4);
        foreach (var (item, itemFiles) in request.Payload.Zip(matched))
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
                    EntityId: request.EntityId,
                    EntityType: request.EntityType));
            }

            s3Result.Add(new UploadedItem(item.Bucket, uploaded));
        }

        await Task.WhenAll(puts);

        var documentIds = await store.AddUploadedDocumentsAsync(records, ct);
        return new UploadDocumentsResult(s3Result, documentIds);
    }

    private async Task PutThrottledAsync(
        SemaphoreSlim throttle, string key, UploadFileUpload file, string bucket, CancellationToken ct)
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
