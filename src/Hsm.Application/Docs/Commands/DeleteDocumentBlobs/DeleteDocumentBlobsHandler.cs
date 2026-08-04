using Hsm.Application.Abstractions;
using Hsm.Application.Ports;

namespace Hsm.Application.Docs.Commands.DeleteDocumentBlobs;

public sealed class DeleteDocumentBlobsHandler(IObjectStorage storage)
    : IRequestHandler<DeleteDocumentBlobsCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeleteDocumentBlobsCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Independent blob deletes run concurrently (bounded), each still
        // best-effort: the frozen deleteFiles swallowed per-object failures
        // (logged only) — DeleteDocumentHandler answered success either way,
        // and this job carries that same posture forward.
        using var throttle = new SemaphoreSlim(4);
        await Task.WhenAll(request.Blobs.Select(async blob =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                await storage.DeleteAsync(blob.Key, blob.Bucket, ct);
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

        return Unit.Value;
    }
}
