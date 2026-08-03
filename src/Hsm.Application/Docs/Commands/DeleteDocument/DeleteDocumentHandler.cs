using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Application.Ports;

namespace Hsm.Application.Docs.Commands.DeleteDocument;

public sealed class DeleteDocumentHandler(IDocumentStore store, IObjectStorage storage, ICurrentPrincipal principal)
    : IRequestHandler<DeleteDocumentCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeleteDocumentCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = principal.Actor ?? throw new UnauthorizedException();
        var userId = Guid.Parse(actor.Id);

        var document = await store.FindWithVersionsAsync(request.Id, userId, ct)
            ?? throw new NotFoundException("Document", request.Id);

        await store.SoftDeleteAsync(request.Id, ct);

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

        return Unit.Value;
    }
}
