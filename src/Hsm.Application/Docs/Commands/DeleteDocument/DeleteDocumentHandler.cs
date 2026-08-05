using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Docs.Commands.DeleteDocument;

/// <summary>
/// <b>Does not delete blobs itself.</b> Object-store
/// deletes are not transactional and have no rollback: if this handler ran
/// inside an ambient transaction alongside OTHER <see cref="DeleteDocumentCommand"/>
/// dispatches (the bulk-delete route wraps a whole batch in one
/// <c>IUnitOfWork.ExecuteInTransactionAsync</c> call so an unknown id rolls the
/// batch back — see <c>DocumentEndpoints.DeleteDocumentsBulk</c>), a blob
/// deleted here would stay deleted even after the DB transaction rolled the row
/// back, leaving a "restored" document whose file no longer exists. Instead this
/// handler only captures each version's storage coordinates before the row is
/// gone; the caller enqueues
/// <see cref="Hsm.Application.Docs.Commands.DeleteDocumentBlobs.DeleteDocumentBlobsCommand"/>
/// strictly after the whole transaction (single delete's own, or the bulk
/// batch's) has committed — the same post-commit-enqueue rule
/// <c>GenerateDocument</c>/<c>SendEmail</c> already follow for their own queued
/// work.
/// </summary>
public sealed class DeleteDocumentHandler(IDocumentStore store, ICurrentPrincipal principal)
    : IRequestHandler<DeleteDocumentCommand, DeleteDocumentResult>
{
    public async Task<DeleteDocumentResult> HandleAsync(DeleteDocumentCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = principal.Actor ?? throw new UnauthorizedException();
        var userId = Guid.Parse(actor.Id);

        var document = await store.FindWithVersionsAsync(request.Id, userId, ct)
            ?? throw new NotFoundException("Document", request.Id);

        await store.SoftDeleteAsync(request.Id, ct);

        var blobs = document.Versions
            .Where(version => version.Storage is not null)
            .Select(version =>
            {
                var (folderName, fileId) = StorageKeys.Split(version.Storage!.Path);
                return new DocumentBlobRef(StorageKeys.MakeKey(folderName, fileId), version.Storage.Bucket);
            })
            .ToList();

        return new DeleteDocumentResult(request.Id, blobs);
    }
}
