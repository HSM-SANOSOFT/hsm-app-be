using Hsm.Application.Abstractions;
using Hsm.Application.Ports;

namespace Hsm.Application.Docs.Queries.PresignDocuments;

public sealed class PresignDocumentsHandler(IObjectStorage storage)
    : IRequestHandler<PresignDocumentsQuery, IReadOnlyList<PresignedItem>>
{
    public async Task<IReadOnlyList<PresignedItem>> HandleAsync(PresignDocumentsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<PresignedItem>();
        foreach (var item in request.Items)
        {
            var files = new List<PresignedFile>();
            foreach (var file in item.Files)
            {
                var key = StorageKeys.MakeKey(file.FolderName, file.FileId);
                var url = await storage.PresignGetAsync(
                    key,
                    request.ExpiresInSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                    request.ContentDisposition ?? "inline",
                    item.Bucket,
                    ct);
                files.Add(new PresignedFile(file.FileId, key, url.AbsoluteUri));
            }

            results.Add(new PresignedItem(item.Bucket, files));
        }

        return results;
    }
}
